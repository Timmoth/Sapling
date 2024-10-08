//using FluentAssertions;
//using Sapling.Engine.Pgn;

//namespace Sapling.Engine.Tests;

//public class ZobristTests
//{
//    [Fact]
//    public void InitialHash_Is_Correct()
//    {
//        var board = BoardStateExtensions.CreateBoardFromFen(Constants.InitialState);
//        board.Hash.Should().Be(10825574554103633524UL);
//    }

//    [Theory]
//    [InlineData(2385735843766215098UL,
//        "1.d4 Nf6 2.c4 c5 3.d5 e6 4.Nc3 exd5 5.cxd5 d6 6.e4 a6 7.a4 g6 8.Nf3 Bg7 9.Be2 O-O\n10.O-O Re8 11.Qc2 Nbd7 12.Nd2 Ne5 13.h3 g5 14.Nc4 g4 15.Nxe5 Rxe5 16.h4 Nh5\n17.g3 Re8 18.Bd3 Qe7 19.Be3 Bd7 20.a5 Rac8 21.b3 Qe5 22.Ne2 Bb5 23.Bxb5 axb5\n24.a6 Ra8 25.a7 Qxe4 26.Qxe4 Rxe4 27.Ra5 Rb4 28.Rb1 c4 29.Bd2 Rxb3 30.Rxb3 cxb3\n31.Rxb5 Rxa7 32.Rxb3 Ra2 33.Rd3 Nf6 34.Nd4 Nd7 35.Bf4 Nc5 36.Rd1 Ne4 37.Re1 Bxd4\n38.Rxe4 Bxf2+ 39.Kf1 h5 40.Bxd6 Ba7 41.Bf4 Kg7 42.d6 Ra5 43.Rb4 Rd5 44.Rxb7 Bc5\n45.Rb5 Kf6 46.d7 Ke7 47.Be3 Kxd7 48.Rxc5 Rd3 49.Ke2 Ra3 50.Rxh5 Ke6 51.Rb5 Ra2+\n52.Kd3 Ra3+ 53.Ke4 Ra4+ 54.Bd4 Ra3 55.Re5+ Kd7 56.Be3 f6 57.Rb5 Ke6 58.Rb6+ Kf7\n59.Kf4 Kg6 60.Bd4 Kh5 61.Rb5+ Kg6 62.Kxg4 Ra4 63.Rd5 f5+ 64.Kf4 Rb4 65.Rd6+ Kh5\n66.Kxf5 Rb3 67.Be5 Rxg3 68.Bf4  1-0")]
//    [InlineData(1974259010437475510UL,
//        "1.e4 e5 2.f4 exf4 3.Nf3 Nf6 4.Nc3 d5 5.exd5 Nxd5 6.Nxd5 Qxd5 7.d4 Be7 8.c4 Qe6+\n9.Kf2 Qf6 10.Bd3 c5 11.d5 O-O 12.Qc2 Qh6 13.Bd2 f5 14.Rae1 Bd6 15.Re2 Nd7\n16.Rhe1 Nf6 17.Bxf5 Ng4+ 18.Bxg4 Bxg4 19.Kg1 Qh5 20.Qd3 Bf5 21.Qb3 b6 22.Bc3 Rae8\n23.Be5 Bxe5 24.Rxe5 Rxe5 25.Rxe5 Re8 26.Qc3 Qf7 27.Qe1 Bd7 28.Qe4 Rxe5 29.Qxe5 Qf5\n30.Qb8+ Kf7 31.h4 Ke7 32.Kh2 Qf6 33.b3 a6 34.a4 h6 35.a5 bxa5 36.Qc7 Qd6\n37.Qxa5 g5 38.Qe1+ Kf6 39.Qc3+ Kg6 40.Qe5 Qf6 41.Qc7 Bf5 42.Ne5+ Kh5 43.hxg5 hxg5\n44.g4+ fxg3+ 45.Kxg3 g4 46.Qf7+ Qxf7 47.Nxf7 Kg6 48.Ne5+ Kg7 49.Kf4 Bc2 50.Kxg4 Bxb3\n51.Kf5 Kf8 52.d6 Ke8 53.Ke6  1-0")]
//    public void ZorbristHashMatches(ulong hash, string pgn)
//    {
//        var (gameState, searcher) = ChessboardHelpers.InitialState();

//        pgn = pgn.Replace("1-0", "")
//            .Replace("0-1", "")
//            .Replace("1/2-1/2", "")
//            .Replace("*", "").Trim();

//        foreach (var move in PgnSplitter.SplitPgnIntoMoves(pgn))
//        {
//            var turns = move.Split(' ');

//            var mov = PgnParser.Parse(turns[0], gameState.LegalMoves);

//            gameState.Apply(mov);

//            if (turns.Length == 1)
//            {
//                break;
//            }

//            mov = PgnParser.Parse(turns[1], gameState.LegalMoves);
//            gameState.Apply(mov);
//        }

//        gameState.Board.Data.Hash.Should().Be(hash);
//    }

//    [Theory]
//    [InlineData("4k3/8/p2PK3/2p1N3/2P5/1b6/8/8",
//        "1.e4 e5 2.f4 exf4 3.Nf3 Nf6 4.Nc3 d5 5.exd5 Nxd5 6.Nxd5 Qxd5 7.d4 Be7 8.c4 Qe6+\n9.Kf2 Qf6 10.Bd3 c5 11.d5 O-O 12.Qc2 Qh6 13.Bd2 f5 14.Rae1 Bd6 15.Re2 Nd7\n16.Rhe1 Nf6 17.Bxf5 Ng4+ 18.Bxg4 Bxg4 19.Kg1 Qh5 20.Qd3 Bf5 21.Qb3 b6 22.Bc3 Rae8\n23.Be5 Bxe5 24.Rxe5 Rxe5 25.Rxe5 Re8 26.Qc3 Qf7 27.Qe1 Bd7 28.Qe4 Rxe5 29.Qxe5 Qf5\n30.Qb8+ Kf7 31.h4 Ke7 32.Kh2 Qf6 33.b3 a6 34.a4 h6 35.a5 bxa5 36.Qc7 Qd6\n37.Qxa5 g5 38.Qe1+ Kf6 39.Qc3+ Kg6 40.Qe5 Qf6 41.Qc7 Bf5 42.Ne5+ Kh5 43.hxg5 hxg5\n44.g4+ fxg3+ 45.Kxg3 g4 46.Qf7+ Qxf7 47.Nxf7 Kg6 48.Ne5+ Kg7 49.Kf4 Bc2 50.Kxg4 Bxb3\n51.Kf5 Kf8 52.d6 Ke8 53.Ke6  1-0")]
//    [InlineData("8/5r2/6k1/8/8/1R6/1p4r1/1K5R",
//        "1.d4 d5 2.c4 c6 3.Nf3 e6 4.Nc3 Nd7 5.cxd5 exd5 6.Bf4 Ne7 7.e3 Ng6 8.Bg3 Nf6\n9.Bd3 Be7 10.h3 O-O 11.Qc2 Bd6 12.Bxd6 Qxd6 13.O-O Ne8 14.Rfe1 Qf6 15.Nh2 Nd6\n16.b4 Bd7 17.b5 Nxb5 18.Bxb5 cxb5 19.Nxd5 Qg5 20.e4 f5 21.Nf3 Qd8 22.Ne5 Rc8\n23.Qe2 Bc6 24.Nxg6 hxg6 25.Nf4 Re8 26.Nxg6 Qg5 27.Ne5 Bxe4 28.g3 Rc2 29.Qxb5 Rec8\n30.Qb3+ Kh7 31.h4 Qd2 32.Qe3 Qxe3 33.Rxe3 Rxa2 34.Rf1 b5 35.Nf7 b4 36.Rd1 Rd2\n37.Ng5+ Kg6 38.Ra1 Ra2 39.Rd1 Bd5 40.Re5 Rd8 41.Nh3 Kf6 42.Rde1 Ra6 43.Nf4 Bf7\n44.d5 Rb6 45.f3 Rd7 46.g4 g6 47.h5 Kg5 48.hxg6 Bxg6 49.Ne6+ Kf6 50.Nf8 Rdd6\n51.Nxg6 Kxg6 52.gxf5+ Kg5 53.Kf2 b3 54.f4+ Kf6 55.Re6+ Kxf5 56.R1e5+ Kg4\n57.Re1 a5 58.f5 Kg5 59.R1e3 b2 60.Rg3+ Kh4 61.Rg1 a4 62.Re4+ Kh5 63.Re3 Rb4\n64.Rh3+ Rh4 65.Rf3 Rxd5 66.f6 Rd2+ 67.Ke3 Rd8 68.f7 Rf8 69.Kd3 Rg4 70.Rh1+ Kg6\n71.Kc2 Rg2+ 72.Kb1 a3 73.Rxa3 Rxf7 74.Rb3  1/2-1/2")]
//    [InlineData("8/5rk1/6r1/p4K2/1p4R1/8/PP6/3R4",
//        "1.d4 Nf6 2.c4 g6 3.Nc3 Bg7 4.e4 d6 5.f3 O-O 6.Be3 e5 7.Nge2 c6 8.Qd2 exd4\n9.Bxd4 Be6 10.Nf4 c5 11.Be3 Nc6 12.Nb5 Ne8 13.O-O-O Qb6 14.Nd5 Bxd5 15.cxd5 Ne5\n16.Bh6 a6 17.Nc3 Qa5 18.Kb1 b5 19.Rc1 c4 20.Bxg7 Nxg7 21.f4 Nd7 22.Qd4 Rfe8\n23.g4 Qb6 24.Qxb6 Nxb6 25.Re1 b4 26.Nd1 Nxd5 27.Bxc4 Nxf4 28.Rhf1 g5 29.h4 h6\n30.hxg5 hxg5 31.Ne3 Ra7 32.Nd5 Nxd5 33.Bxd5 Re5 34.Rc1 Ne8 35.Rc6 Kg7 36.Rfc1 Nf6\n37.Rxd6 Nxg4 38.Rg1 Nf6 39.Kc2 g4 40.Kd3 Rg5 41.Ke3 Nh5 42.Bc4 a5 43.Be2 Nf6\n44.Kf4 Rg6 45.e5 Nh5+ 46.Kf5 Re7 47.Bxg4 f6 48.exf6+ Nxf6 49.Rdd1 Nxg4 50.Rxg4 Rf7+  0-1")]
//    [InlineData("8/p1r5/k6p/1P1Q4/2N4P/3K4/8/8",
//        "1. d4 d5 2. Nc3 Bf5 3. Bf4 e6 4. f3 Bd6 5. Qd2 Ne7 6. g4 Bg6 7. O-O-O Nbc6 8. Bxd6 Qxd6 9. e4 O-O-O 10. e5 Qb4 11. a3 Qa5 12. Nge2 Kb8 13. Nf4 Nc8 14. h4 h6 15. Nxg6 fxg6 16. Ne2 Nb6 17. Qxa5 Nxa5 18. Nf4 Rhe8 19. Nxg6 Nac4 20. b3 Ne3 21. Rd3 Nxf1 22. Rxf1 Nd7 23. f4 c5 24. c3 Rc8 25. Kb2 c4 26. Rdf3 cxb3 27. Kxb3 Nb6 28. f5 Nc4 29. R1f2 Rc6 30. Ra2 exf5 31. gxf5 Rb6+ 32. Kc2 Ra6 33. a4 b5 34. a5 Rxa5 35. Rxa5 Nxa5 36. f6 gxf6 37. exf6 b4 38. cxb4 Nc6 39. f7 Rc8 40. f8=Q Nxd4+ 41. Kd3 Nxf3 42. Qf4+ Kb7 43. Qxf3 Kb6 44. Qxd5 Rc6 45. Ne5 Rc7 46. Nc4+ Ka6 47. b5#")]
//    [InlineData("8/8/8/3K4/3B4/7p/7k/6Q1",
//        "1. e4 g6 2. d4 Bg7 3. Nc3 c6 4. f4 d5 5. e5 Nh6 6. Bd3 O-O 7. Nf3 Bf5 8. O-O f6 9. Be3 Nd7 10. Bxf5 Nxf5 11. Bf2 fxe5 12. fxe5 e6 13. Qd2 c5 14. Rae1 cxd4 15. Nxd4 Bh6 16. Qe2 Nxd4 17. Bxd4 Qa5 18. Qg4 Qa6 19. Nxd5 Rxf1+ 20. Rxf1 Nxe5 21. Bxe5 exd5 22. Bd4 Qd6 23. Rf6 Qe7 24. Re6 Qf7 25. Qe2 Qf4 26. Re8+ Rxe8 27. Qxe8+ Qf8 28. Qe6+ Qf7 29. Qc8+ Bf8 30. Qd8 Qe6 31. h3 Kf7 32. Qc7+ Qe7 33. Qxe7+ Bxe7 34. Bxa7 Ke6 35. Bd4 Kf5 36. Kf2 Ke4 37. Bg7 d4 38. Ke2 Bg5 39. Bf8 Bc1 40. b3 b5 41. Bb4 Bf4 42. a4 bxa4 43. bxa4 Bb8 44. a5 Ba7 45. a6 Kf4 46. Kd3 Kg3 47. Kc4 Kxg2 48. Bc5 Bb8 49. a7 Bxa7 50. Bxa7 Kxh3 51. Bxd4 g5 52. Kd5 g4 53. c4 h5 54. c5 h4 55. c6 Kh2 56. c7 g3 57. c8=Q h3 58. Qc1 g2 59. Qe1 g1=B 60. Qxg1#")]
//    public void Produces_Correct_Fen_String(string fen, string pgn)
//    {
//        var (gameState, searcher) = ChessboardHelpers.InitialState();

//        pgn = pgn.Replace("1-0", "")
//            .Replace("0-1", "")
//            .Replace("1/2-1/2", "")
//            .Replace("*", "").Trim();

//        foreach (var move in PgnSplitter.SplitPgnIntoMoves(pgn))
//        {
//            var turns = move.Split(' ');

//            var mov = PgnParser.Parse(turns[0], gameState.LegalMoves);

//            gameState.Apply(mov);

//            if (turns.Length == 1)
//            {
//                break;
//            }

//            mov = PgnParser.Parse(turns[1], gameState.LegalMoves);

//            gameState.Apply(mov);
//        }

//        // gameState.Board.Pieces.ArrayToFen().Should().Be(fen);
//    }
//}