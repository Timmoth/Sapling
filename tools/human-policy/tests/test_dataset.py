import json
import io
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

import chess

sys.path.insert(0, str(Path(__file__).parents[1]))
from dataset import encode_board, encode_move, encode_position, encode_rating
from extract_pgn import (
    game_window, headers_from_pgn_text, is_standard_rated_rapid, iter_pgn_text_games,
    iter_processed_games, open_pgn,
    rapid_time_control, rating_band, split_for,
)


FIXTURES = Path(__file__).with_name("golden_cases.json")


class DatasetEncodingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixtures = json.loads(FIXTURES.read_text())

    def test_board_and_move_encoders_match_shared_golden_fixtures(self):
        self.assertEqual(self.fixtures["schema_version"], 1)
        for case in self.fixtures["cases"]:
            with self.subTest(case=case["name"]):
                board = chess.Board(case["fen"])
                encoded_board = encode_board(board)
                expected = set()
                for channel, squares in case["piece_squares"].items():
                    expected.update(int(channel) * 64 + square for square in squares)
                for channel in case["full_planes"]:
                    expected.update(channel * 64 + square for square in range(64))
                if case["ep_square"] is not None:
                    expected.add(17 * 64 + case["ep_square"])
                self.assertEqual(
                    {i for i, value in enumerate(encoded_board) if value}, expected
                )

                legal_moves = list(board.legal_moves)
                for uci, expected_indices in case["moves"].items():
                    move = chess.Move.from_uci(uci)
                    self.assertIn(move, legal_moves)
                    encoded = encode_move(move, root_score=0, best_root_score=0)
                    self.assertEqual(
                        [i for i, value in enumerate(encoded[:132]) if value], expected_indices
                    )

        for case in self.fixtures["engine_feature_cases"]:
            with self.subTest(engine_case=case):
                board = chess.Board(case["fen"])
                move = chess.Move.from_uci(case["move"])
                features = encode_move(move, case["root_score"], case["best_root_score"])
                self.assertAlmostEqual(features[132], case["expected"][0], places=7)
                self.assertAlmostEqual(features[133], case["expected"][1], places=7)

    def test_training_row_requires_legal_label_and_encodes_rating(self):
        board = chess.Board()
        moves = list(board.legal_moves)
        selected = chess.Move.from_uci("e2e4")
        engine_scores = {move.uci(): 0 for move in moves}
        boards, candidates, rating, target = encode_position(board, moves, selected, 1500, engine_scores)
        self.assertEqual(len(boards), 18 * 64)
        self.assertEqual(len(candidates), len(moves))
        self.assertEqual(target, moves.index(selected))
        self.assertEqual(rating, 0.0)
        with self.assertRaisesRegex(ValueError, "absent from legal candidates"):
            encode_position(board, moves, chess.Move.from_uci("e2e5"), 1500, engine_scores)
        with self.assertRaisesRegex(ValueError, "score set must exactly match"):
            encode_position(board, moves, selected, 1500, {"e2e4": 0})

    def test_rating_range_is_explicit(self):
        self.assertAlmostEqual(encode_rating(500), -1.4285714285714286)
        self.assertEqual(encode_rating(800), -1.0)
        self.assertEqual(encode_rating(2200), 1.0)
        for rating in (499, 2201):
            with self.subTest(rating=rating), self.assertRaises(ValueError):
                encode_rating(rating)

    def test_inclusive_maximum_rating_stays_in_final_calibration_band(self):
        self.assertEqual(rating_band(2199), "2000-2199")
        self.assertEqual(rating_band(2200), "2000-2199")

    def test_chess960_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Chess960 is excluded"):
            encode_board(chess.Board.from_chess960_pos(0))

    def test_rapid_filter_uses_lichess_estimated_duration(self):
        self.assertTrue(rapid_time_control("480+0"))
        self.assertTrue(rapid_time_control("600+0"))
        self.assertTrue(rapid_time_control("300+5"))
        self.assertFalse(rapid_time_control("479+0"))
        self.assertFalse(rapid_time_control("1500+0"))
        self.assertFalse(rapid_time_control("40/900:900+30"))

    def test_game_split_is_deterministic(self):
        self.assertEqual(split_for("game-42"), split_for("game-42"))
        self.assertIn(split_for("game-42"), {"train", "validation", "test"})

    def test_pgn_input_dash_reads_from_stdin(self):
        stdin = io.StringIO('[Event "Rated Rapid game"]\n')
        with patch("extract_pgn.sys.stdin", stdin):
            with open_pgn(Path("-")) as stream:
                self.assertEqual(stream.read(), '[Event "Rated Rapid game"]\n')

    def test_fast_pgn_header_scan_preserves_complete_accepted_game_text(self):
        text = (
            '[Event "Rated Rapid game"]\n[Site "https://lichess.org/abc"]\n'
            '[WhiteElo "1500"]\n[BlackElo "1510"]\n[TimeControl "600+0"]\n\n'
            '1. e4 e5 2. Nf3 Nc6 1-0\n\n'
            '[Event "Rated Blitz game"]\n[TimeControl "180+0"]\n\n'
            '1. d4 d5 0-1\n'
        )
        games = list(iter_pgn_text_games(io.StringIO(text)))
        chunked_games = list(iter_pgn_text_games(io.StringIO(text), chunk_size=11))
        self.assertEqual(len(games), 2)
        self.assertEqual(chunked_games, games)
        headers = headers_from_pgn_text(games[0])
        self.assertEqual(is_standard_rated_rapid(headers), (True, ""))
        parsed = chess.pgn.read_game(io.StringIO(games[0]))
        self.assertEqual([move.uci() for move in parsed.mainline_moves()],
                         ["e2e4", "e7e5", "g1f3", "b8c6"])
        self.assertEqual(is_standard_rated_rapid(headers_from_pgn_text(games[1]))[1],
                         "not_rapid_or_unknown_time_control")

    def test_game_window_skips_and_limits_archive_order_without_reordering(self):
        games = iter(("game-0", "game-1", "game-2", "game-3", "game-4"))
        self.assertEqual(list(game_window(games, skip_games=2, max_games=2)),
                         ["game-2", "game-3"])
        self.assertEqual(list(game_window(iter(range(4)), skip_games=4, max_games=0)), [])

    def test_parallel_game_parsing_matches_serial_and_preserves_archive_order(self):
        text = (
            '[Event "Rated Rapid game"]\n[Site "https://lichess.org/rapid-a"]\n'
            '[WhiteElo "1500"]\n[BlackElo "1510"]\n[TimeControl "600+0"]\n\n'
            '1. e4 e5 2. Nf3 Nc6 1-0\n\n'
            '[Event "Casual game"]\n[Site "https://lichess.org/casual-b"]\n'
            '[WhiteElo "1500"]\n[BlackElo "1510"]\n[TimeControl "600+0"]\n\n'
            '1. d4 d5 0-1\n\n'
            '[Event "Rated Rapid game"]\n[Site "https://lichess.org/rapid-c"]\n'
            '[WhiteElo "1600"]\n[BlackElo "1590"]\n[TimeControl "600+0"]\n\n'
            '1. c4 e5 2. Nc3 Nf6 1/2-1/2\n'
        )
        games = list(iter_pgn_text_games(io.StringIO(text)))
        serial = list(iter_processed_games(games, workers=1, minimum=800, maximum=2200))
        parallel = list(iter_processed_games(
            games, workers=2, minimum=800, maximum=2200, batch_size=2
        ))

        self.assertEqual(parallel, serial)
        self.assertEqual([rows[0]["game_id"] if rows else reason for rows, reason in parallel],
                         ["rapid-a", "skip_not_rated", "rapid-c"])
        self.assertEqual(parallel[0][0][0]["game_date"], "")

    def test_rating_filter_retains_in_range_mover_against_out_of_range_opponent(self):
        text = ('[Event "Rated Rapid game"]\n[Site "https://lichess.org/boundary"]\n'
                '[WhiteElo "2100"]\n[BlackElo "2300"]\n[TimeControl "600+0"]\n\n'
                '1. e4 e5 2. Nf3 Nc6 1/2-1/2\n')
        rows, reason = list(iter_processed_games([text], workers=1, minimum=500, maximum=2200))[0]
        self.assertEqual(reason, "")
        self.assertEqual([row["move"] for row in rows], ["e2e4", "g1f3"])
        self.assertEqual([row["ply"] for row in rows], [0, 2])
        self.assertTrue(all(row["rating"] == 2100 for row in rows))
        rejected, reason = list(iter_processed_games(
            [text.replace('"2100"', '"2400"')], workers=1, minimum=500, maximum=2200))[0]
        self.assertIsNone(rejected)
        self.assertEqual(reason, "skip_missing_or_out_of_range_rating")

    def test_standard_rated_rapid_header_filter(self):
        game = chess.pgn.Game()
        game.headers["Event"] = "Rated Rapid game"
        game.headers["TimeControl"] = "600+0"
        self.assertEqual(is_standard_rated_rapid(game), (True, ""))
        game.headers["TimeControl"] = "180+0"
        self.assertEqual(is_standard_rated_rapid(game), (False, "not_rapid_or_unknown_time_control"))


if __name__ == "__main__":
    unittest.main()
