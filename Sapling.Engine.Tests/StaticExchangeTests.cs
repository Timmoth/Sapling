using FluentAssertions;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Tests;

public class StaticExchangeTests
{
    [Theory]
    [InlineData("5rk1/1pp2q1p/p1pb4/8/3P1NP1/2P5/1P1BQ1P1/5RK1 b - -", "d6f4", "Nb")]
    [InlineData("2r1r1k1/pp1bppbp/3p1np1/q3P3/2P2P2/1P2B3/P1N1B1PP/2RQ1RK1 b - -", "d6e5", "PpP")]
    [InlineData("4q3/1p1pr1k1/1B2rp2/6p1/p3PP2/P3R1P1/1P2R1K1/4Q3 b - -", "e6e4", "PrRr")]
    //[InlineData("3r3k/3r4/2n1n3/8/3p4/2PR4/1B1Q4/3R3K w - -", "d3d4", "pRnPnB")]
    [InlineData("3N4/2K5/2n5/1k6/8/8/8/8 b - -", "c6d8", "nN")]
    [InlineData("r1bqkb1r/2pp1ppp/p1n5/1p2p3/3Pn3/1B3N2/PPP2PPP/RNBQ1RK1 b kq -", "c6d4", "PnNp")]
    [InlineData("6k1/1pp4p/p1pb4/6q1/3P1pRr/2P4P/PP1Br1P1/5RKN w - -", "f1f4", "pR")]
    [InlineData("4R3/2r3p1/5bk1/1p1r3p/p2PR1P1/P1BK1P2/1P6/8 b - -", "h5g4", "Pp")]
    public unsafe void GetKingPosition_Returns_CorrectPosition(string fen, string uciMove, string exchange)
    {
        // Given
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        var searcher = new Searcher(Array.Empty<Transposition>());

        var moves = new List<uint>();
        board.Data.GenerateLegalMoves(moves, true);
        var move = Assert.Single(moves.Where(m => m.ToUciMoveName() == uciMove));

        Span<ulong> occupancyBitBoards = stackalloc ulong[8]
        {
            board.Data.WhitePieces, board.Data.BlackPieces,
            board.Data.BlackPawns | board.Data.WhitePawns,
            board.Data.BlackKnights | board.Data.WhiteKnights,
            board.Data.BlackBishops | board.Data.WhiteBishops,
            board.Data.BlackRooks | board.Data.WhiteRooks,
            board.Data.BlackQueens | board.Data.WhiteQueens,
            board.Data.BlackKings | board.Data.WhiteKings
        };

        Span<short> captures = stackalloc short[32];

        // When
        var seeScore = board.Data.StaticExchangeEvaluation(occupancyBitBoards, captures, move);

        // Then

        var expected = 0;
        for (var i = 0; i < exchange.Length; i++)
        {
            var v = PieceValues.PieceValue[ChatToPiece(exchange[i])];
            expected += i % 2 == 0 ? v : -v;
        }

        seeScore.Should().Be(expected);
    }

    public static byte ChatToPiece(char c)
    {
        return c switch
        {
            'p' => 1,
            'P' => 2,
            'n' => 3,
            'N' => 4,
            'b' => 5,
            'B' => 6,
            'r' => 7,
            'R' => 8,
            'q' => 9,
            'Q' => 10,
            'k' => 11,
            'K' => 12
        };
    }
}