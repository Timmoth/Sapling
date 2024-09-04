using FluentAssertions;
using iBlunder.Engine.Evaluation;
using iBlunder.Engine.MoveGen;
using iBlunder.Engine.Search;
using iBlunder.Engine.Transpositions;

namespace iBlunder.Engine.Tests;

public class NNUETests
{
    [Theory]
    [InlineData(
        "c2c3 c7c6 a2a4 d7d5 g1f3 e7e6 h2h4 g8e7 e2e4 d5e4 b1a3 e4f3 g2g4 h7h5 g4g5 e7g6 d1f3 f8a3 a1a3 e6e5 a3b3 e8g8 a4a5 d8a5 f1g2 b8d7 f3h5 d7c5 h5d1 c5d3 e1f1 a5c5 d1f3 d3c1 b3b4 c1a2 d2d4 e5d4 b4d4 a2c1 g2h3 c8h3 h1h3 a8d8 d4b4 c1b3 f3e2 f8e8 e2g4 b3d2 f1g1 a7a5 b4d4 d8d4 g4d4 c5d4 c3d4 e8e1 g1g2 g6f4 g2g3 f4h3 f2f4 e1e3 g3h2 e3b3 f4f5 a5a4 f5f6 g7g6 h4h5 g6h5 g5g6 f7g6 d4d5 c6d5 f6f7 g8f8 h2g2 h5h4 g2h1 b3g3 h1h2 d2f1 h2h1 h3f2")]
    [InlineData(
        "b2b3 c7c6 g1f3 g7g6 c1b2 g8f6 b1c3 d7d5 e2e3 f8g7 f1d3 e8g8 e1g1 b8d7 a2a4 d7c5 d3e2 a7a5 d2d4 c5a6 e2a6 a8a6 f3e5 a6a8 f1e1 d8c7 c3e2 f6e8 h2h4 g7e5 d4e5 c6c5 d1d5 c8e6 d5f3 a8d8 e2f4 c7b6 b2c3 e8c7 f4e6 c7e6 e1b1 g8g7 b1b2 g6g5 b3b4 c5b4 a1b1 b6c7 c3e1 g5h4 f3g4 g7h8 e1b4 a5b4 b2b4 c7c2 b4b2 c2c5 g4h4 f8g8 b2b7 g8g5 g2g3 h8g7 h4b4 g5e5 b4c5 e6c5 b7b5 d8d6 a4a5 c5d7 b5e5 d7e5 b1b7 d6a6 b7e7 a6a5 e7b7 h7h5 b7b2 g7g6 e3e4 a5a3 b2b8 g6g7 b8b7 e5g4 b7b2 a3c3 g1g2 c3c4 g2h3 c4a4 f2f3 g4e5 b2b5 e5f3 b5h5 a4e4 h5a5 g7g6 h3g2 f3g5 g2f2 g6h5 a5a8 h5g4 a8a3 e4b4 f2e2 f7f5 a3a6 g4g3 a6a3 g3g2 a3a5 g5f3 e2d3 f5f4 a5a2 g2g3 a2a5 b4d4 d3c3 g3f2 a5a1 f2e2 a1a2 d4d2 a2a8 d2d3 c3b2 f3d4 a8e8 e2d2")]
    public void ApplyUnapplyMatchesInitialState(string moves)
    {
        // Given
        var moveList = moves.Split(' ');
        var board = BoardStateExtensions.CreateBoardFromArray(Constants.InitialState);
        var searcher = new Searcher(Array.Empty<Transposition>());
        searcher.Init(0, board);

        // When
        ApplyUnApply(board, searcher, moveList, 0);

        // Then
        var initialState = BoardStateExtensions.CreateBoardFromArray(Constants.InitialState);
        board.IsSame(initialState).Should().BeTrue();
    }

    [Theory]
    [InlineData(
        "c2c3 c7c6 a2a4 d7d5 g1f3 e7e6 h2h4 g8e7 e2e4 d5e4 b1a3 e4f3 g2g4 h7h5 g4g5 e7g6 d1f3 f8a3 a1a3 e6e5 a3b3 e8g8 a4a5 d8a5 f1g2 b8d7 f3h5 d7c5 h5d1 c5d3 e1f1 a5c5 d1f3 d3c1 b3b4 c1a2 d2d4 e5d4 b4d4 a2c1 g2h3 c8h3 h1h3 a8d8 d4b4 c1b3 f3e2 f8e8 e2g4 b3d2 f1g1 a7a5 b4d4 d8d4 g4d4 c5d4 c3d4 e8e1 g1g2 g6f4 g2g3 f4h3 f2f4 e1e3 g3h2 e3b3 f4f5 a5a4 f5f6 g7g6 h4h5 g6h5 g5g6 f7g6 d4d5 c6d5 f6f7 g8f8 h2g2 h5h4 g2h1 b3g3 h1h2 d2f1 h2h1 h3f2",
        "5k2/1p3P2/6p1/3p4/p6p/6r1/1P3n2/5n1K w - -")]
    [InlineData(
        "b2b3 c7c6 g1f3 g7g6 c1b2 g8f6 b1c3 d7d5 e2e3 f8g7 f1d3 e8g8 e1g1 b8d7 a2a4 d7c5 d3e2 a7a5 d2d4 c5a6 e2a6 a8a6 f3e5 a6a8 f1e1 d8c7 c3e2 f6e8 h2h4 g7e5 d4e5 c6c5 d1d5 c8e6 d5f3 a8d8 e2f4 c7b6 b2c3 e8c7 f4e6 c7e6 e1b1 g8g7 b1b2 g6g5 b3b4 c5b4 a1b1 b6c7 c3e1 g5h4 f3g4 g7h8 e1b4 a5b4 b2b4 c7c2 b4b2 c2c5 g4h4 f8g8 b2b7 g8g5 g2g3 h8g7 h4b4 g5e5 b4c5 e6c5 b7b5 d8d6 a4a5 c5d7 b5e5 d7e5 b1b7 d6a6 b7e7 a6a5 e7b7 h7h5 b7b2 g7g6 e3e4 a5a3 b2b8 g6g7 b8b7 e5g4 b7b2 a3c3 g1g2 c3c4 g2h3 c4a4 f2f3 g4e5 b2b5 e5f3 b5h5 a4e4 h5a5 g7g6 h3g2 f3g5 g2f2 g6h5 a5a8 h5g4 a8a3 e4b4 f2e2 f7f5 a3a6 g4g3 a6a3 g3g2 a3a5 g5f3 e2d3 f5f4 a5a2 g2g3 a2a5 b4d4 d3c3 g3f2 a5a1 f2e2 a1a2 d4d2 a2a8 d2d3 c3b2 f3d4 a8e8 e2d2",
        "4R3/8/8/8/3n1p2/3r4/1K1k4/8 w - -")]
    public void ApplyMatchesFen(string moves, string fen)
    {
        // Given
        var moveList = moves.Split(' ');
        var board = BoardStateExtensions.CreateBoardFromArray(Constants.InitialState);

        // When
        Apply(board, moveList, 0);

        // Then
        board.ToFen().Should().Be(fen);
    }

    [Theory]
    [InlineData(1, 0, 64 * 6, 56)]
    [InlineData(2, 0, 0, 440)]
    public void FeatureIndex(int piece, int square, int expectedWhite, int expectedBlack)
    {
        var (bIndex, wIndex) = NnueEvaluator.FeatureIndices(piece, square);
        bIndex.Should().Be(expectedBlack);
        wIndex.Should().Be(expectedWhite);
    }

    public void ApplyUnApply(BoardState board, Searcher searcher, string[] moves, int moveIndex)
    {
        if (moveIndex >= moves.Length)
        {
            return;
        }

        var oldHash = board.Hash;
        var oldEnpassant = board.EnPassantFile;
        var prevInCheck = board.InCheck;
        var prevCastleRights = board.CastleRights;
        var prevFiftyMoveCounter = board.FiftyMoveCounter;

        var validMoves = new List<uint>();
        board.GenerateLegalMoves(validMoves, false);
        var moveString = moves[moveIndex];

        var move = validMoves.FirstOrDefault(m => m.ToUciMoveName() == moveString);
        board.PartialApply(move);
        board.UpdateCheckStatus();
        board.FinishApply(move, oldEnpassant, prevCastleRights);
        ApplyUnApply(board, searcher, moves, moveIndex + 1);
        board.PartialUnApply(move, oldHash, oldEnpassant, prevInCheck, prevCastleRights, prevFiftyMoveCounter);
        board.FinishUnApplyMove(move, oldEnpassant);
    }

    public void Apply(BoardState board, string[] moves, int moveIndex)
    {
        if (moveIndex >= moves.Length)
        {
            return;
        }

        var oldEnpassant = board.EnPassantFile;
        var prevCastleRights = board.CastleRights;


        var validMoves = new List<uint>();
        board.GenerateLegalMoves(validMoves, false);
        var moveString = moves[moveIndex];

        var move = validMoves.FirstOrDefault(m => m.ToUciMoveName() == moveString);
        board.PartialApply(move);
        board.UpdateCheckStatus();
        board.FinishApply(move, oldEnpassant, prevCastleRights);
        Apply(board, moves, moveIndex + 1);
    }
}