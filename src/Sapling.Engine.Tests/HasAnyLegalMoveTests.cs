using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

public unsafe class HasAnyLegalMoveTests
{
    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1")]
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")]
    [InlineData("4k3/P7/8/8/8/8/8/4K3 w - - 0 1")]
    [InlineData("k7/8/1KQ5/8/8/8/8/8 b - - 0 1")]
    [InlineData("r2qkbBr/7p/Pn3np1/2p4P/p3p1b1/B2p1NP1/2PQ1P2/3RK1R1 w - - 0 73")]
    public void MatchesFullGeneratorOnSpecialPositions(string fen)
    {
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        Assert.Equal(HasAnyLegalMoveFromFullList(ref board), board.HasAnyLegalMove());
    }

    [Fact]
    public void MatchesFullGeneratorAcrossDeterministicLegalWalks()
    {
        var random = new Random(20261013);
        for (var game = 0; game < 8; game++)
        {
            using var state = GameState.InitialState();
            for (var ply = 0; ply < 48; ply++)
            {
                var board = state.Board;
                Assert.Equal(HasAnyLegalMoveFromFullList(ref board), board.HasAnyLegalMove());

                var legalMoves = state.LegalMoves;
                if (legalMoves.Count == 0)
                    break;
                state.Apply(legalMoves[random.Next(legalMoves.Count)]);
            }
        }
    }

    private static bool HasAnyLegalMoveFromFullList(ref BoardStateData board)
    {
        var moves = stackalloc uint[218];
        var count = board.GeneratePseudoLegalMoves(moves, captureOnly: false);
        for (var i = 0; i < count; i++)
        {
            var candidate = board;
            var legal = board.WhiteToMove
                ? candidate.PartialApplyWhite(moves[i])
                : candidate.PartialApplyBlack(moves[i]);
            if (legal)
                return true;
        }
        return false;
    }
}
