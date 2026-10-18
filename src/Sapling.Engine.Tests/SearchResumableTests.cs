using Sapling.Engine;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

/// <summary>
/// the resumable browser driver must reach the requested depth across many
/// bounded slices (not one cold pass), must not treat slice exhaustion as a cancellation,
/// and must honour an in-search stop. These guard the host's slice loop.
/// </summary>
public unsafe class SearchResumableTests
{
    [Fact]
    public void ResumableSlice_ReachesRequestedDepthAcrossSmallSlices()
    {
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);

        searcher.BeginResumableSearch(state);

        Searcher.ResumableResult last = default;
        var slices = 0;
        while (slices < 100000)
        {
            last = searcher.ResumableSlice(maxDepth: 8, sliceNodeBudget: 1500, sliceMs: 0);
            if (last.Done)
                break;
            slices++;
        }

        Assert.True(last.Done);
        Assert.Equal(8, last.Depth);
        Assert.NotEqual(0u, last.BestMove);
        Assert.Contains(last.BestMove, state.LegalMoves);
    }

    [Fact]
    public void ResumableSlice_SliceExhaustionIsNotACancellation()
    {
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);

        searcher.BeginResumableSearch(state);

        var depths = new List<int>();
        var guard = 0;
        while (guard++ < 100000)
        {
            var r = searcher.ResumableSlice(maxDepth: 7, sliceNodeBudget: 800, sliceMs: 0);
            Assert.False(r.Stopped); // slice exhaustion must never report a stop
            depths.Add(r.Depth);
            if (r.Done)
                break;
        }

        for (var i = 1; i < depths.Count; i++)
            Assert.True(depths[i] >= depths[i - 1]);
        Assert.Equal(7, depths[^1]);
    }

    [Fact]
    public void ResumableSlice_StopFinalizesTheSession()
    {
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);

        searcher.BeginResumableSearch(state);
        searcher.ResumableSlice(maxDepth: 12, sliceNodeBudget: 2000, sliceMs: 0);

        searcher.Stop();
        var r = searcher.ResumableSlice(maxDepth: 12, sliceNodeBudget: 2000, sliceMs: 0);

        Assert.True(r.Stopped);
        Assert.True(r.Done);
        Assert.NotEqual(0u, r.BestMove);
    }

    // Regression: a slice that exhausts before completing an iteration must not publish a
    // partial (depth>=1) bound at the root. BeginResumable's depth-0 init search leaves a depth-0
    // exact entry; a 1-node budget slice cannot finish depth 1, so the root must stay depth-0 exact.
    [Fact]
    public void ResumableSlice_TinyBudgetDoesNotPublishPartialRootBound()
    {
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);

        searcher.BeginResumableSearch(state);

        var rootIndex = state.Board.Hash & ((ulong)searcher.TTSize - 1);
        ref var rootEntry = ref searcher.Transpositions[rootIndex];
        Assert.Equal(state.Board.Hash, rootEntry.FullHash);
        Assert.Equal(0, rootEntry.Depth);

        var r = searcher.ResumableSlice(maxDepth: 8, sliceNodeBudget: 1, sliceMs: 0);
        Assert.Equal(0, r.Depth);
        Assert.False(r.Stopped);

        // The interrupted depth-1 iteration must not publish a partial bound: the root entry stays
        // the depth-0 entry left by the init search, never a depth>=1 Alpha/Beta bound.
        Assert.True(rootEntry.Depth == 0, "an interrupted iteration must not publish a partial TT bound");
    }

    // Regression: Abort() is a sticky shutdown and must refuse a later resumable search even
    // when no worker was active at the moment of the abort.
    [Fact]
    public void Abort_RefusesLaterResumableSearch()
    {
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);

        searcher.BeginResumableSearch(state);
        searcher.ResumableSlice(maxDepth: 4, sliceNodeBudget: 1000, sliceMs: 0);
        searcher.EndResumableSearch();

        searcher.Abort();

        var act = () => searcher.BeginResumableSearch(state);
        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("aborted", ex.Message);
    }
}
