using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

public unsafe class ThreatHistoryTests
{
    [Fact]
    public void WholeAttackMapMatchesIndependentSquareQueriesDuringLegalPlayouts()
    {
        // Covers pawn edge files, both sides, slider blockers and pinned attackers.
        string[] fens = {
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "4k3/1p4p1/8/2b2n2/2N2B2/8/1P4P1/4K3 w - - 0 1",
            "4k3/4n3/8/8/8/8/p6P/4R1K1 w - - 0 1"
        };
        var random = new Random(20261020);
        foreach (var fen in fens)
        {
            using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
            for (int ply = 0; ply < 100; ply++)
            {
                var board = state.Board;
                for (int side = 0; side < 2; side++)
                {
                    board.WhiteToMove = side == 0;
                    ulong attacks = Searcher.EnemyAttacks(&board);
                    for (int square = 0; square < 64; square++)
                    {
                        bool expected = board.WhiteToMove ? board.IsAttackedByBlack(square) : board.IsAttackedByWhite(square);
                        Assert.Equal(expected, (attacks & (1UL << square)) != 0);
                    }
                }
                var moves = state.LegalMoves;
                if (moves.Count == 0) break;
                state.Apply(moves[random.Next(moves.Count)]);
            }
        }
    }

    [Fact]
    public void SourceAndDestinationThreatContextsStaySeparateAndReset()
    {
        using var owner = new ParallelSearcher(1024);
        var worker = owner.Searchers[0];
        uint winner = MoveExtensions.EncodeNormalMove(Constants.WhiteKnight, 1, 18);
        ulong source = 1UL << 1, destination = 1UL << 18;
        worker.UpdateThreatHistories(true, source, winner, null, 0, 5);
        Assert.True(worker.ThreatScore(true, source, winner) > 0);
        Assert.Equal(0, worker.ThreatScore(true, destination, winner));
        Assert.Equal(0, worker.ThreatScore(true, source | destination, winner));
        Assert.Equal(0, worker.ThreatScore(true, 0, winner));
        Assert.Equal(0, worker.ThreatScore(false, source, winner));
        owner.NewGame();
        Assert.Equal(0, worker.ThreatScore(true, source, winner));
    }

    [Fact]
    public void SearchLearnsAndReturnsALegalMove()
    {
        using var state = GameState.InitialState();
        using var owner = new ParallelSearcher(4096);
        var result = owner.DepthBoundSearch(state, 7);
        Assert.Contains(result.move[0], state.LegalMoves);
        Assert.Contains(new ReadOnlySpan<short>(owner.Searchers[0].ThreatHistory,
            Searcher.ThreatHistoryLength).ToArray(), value => value != 0);
    }
}
