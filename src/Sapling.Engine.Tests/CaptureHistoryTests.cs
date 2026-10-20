using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

public unsafe class CaptureHistoryTests
{
    [Fact]
    public void OnlyCompletedSearchedCapturesReceiveMalus()
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(
            "4k3/8/3p1p2/4P3/8/8/8/4K3 w - - 0 1"));
        using var owner = new ParallelSearcher(1024);
        var worker = owner.Searchers[0];
        var captures = state.LegalMoves.Where(m => m.IsCapture()).ToArray();
        Assert.Equal(2, captures.Length);
        uint* searched = stackalloc uint[] { captures[1] };
        worker.UpdateCaptureHistories(captures[0], searched, 1, 5);
        Assert.True(worker.CaptureHistory[Searcher.CaptureHistoryIndex(captures[0])] > 0);
        Assert.True(worker.CaptureHistory[Searcher.CaptureHistoryIndex(captures[1])] < 0);
        owner.NewGame();
        Assert.DoesNotContain(new ReadOnlySpan<short>(worker.CaptureHistory,
            Searcher.CaptureHistoryLength).ToArray(), value => value != 0);
    }

    [Fact]
    public void RealSearchLearnsCaptureOrderingAndKeepsLegalBestMove()
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(
            "r3k2r/ppp2ppp/2n5/3qp3/8/2NP1N2/PPP2PPP/R2QK2R w KQkq - 0 1"));
        using var owner = new ParallelSearcher(4096);
        var result = owner.DepthBoundSearch(state, 7);
        Assert.Contains(result.move[0], state.LegalMoves);
        Assert.Contains(new ReadOnlySpan<short>(owner.Searchers[0].CaptureHistory,
            Searcher.CaptureHistoryLength).ToArray(), value => value != 0);
    }
}
