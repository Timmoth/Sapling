using System;
using System.Threading;
using System.Threading.Tasks;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;
using Xunit;

namespace Sapling.Engine.Tests;

// cancellation contract:
// one sticky stop flag; NodesVisited >= nodeLimit is the single boundary; only completed
// iterations may replace the reported result; interrupted iterations publish no TT entries;
// very small limits still return a legal move.
public class CancellationSemanticsTests
{
    // Complex enough that the node budget is always the binding constraint.
    private const string ComplexFen = "r3k2r/2pb1ppp/2pp1q2/p7/1nP1B3/1P2P3/P2N1PPP/R2QK2R w KQkq a6 0 14";

    private static GameState State() => new(BoardStateExtensions.CreateBoardFromFen(ComplexFen));

    [Fact]
    public void TimeBoundSearch_WithAmpleBudget_CompletesMoreThanFirstIteration()
    {
        using var state = State();
        using var owner = new ParallelSearcher(1024 * 1024);
        var (pv, depth, _, _, _) = owner.TimeBoundSearch(state, 1000);
        Assert.NotEmpty(pv);
        Assert.True(depth > 1, "A future deadline must not terminate the search after its first iteration.");
    }

    [Fact]
    public void NodeLimit_BoundaryIsAtOrAfterLimit_AndDeterministic()
    {
        const int nodeLimit = 20_000;
        using var state = State();

        (System.Collections.Generic.List<uint> pv, int score, long nodes) Run()
        {
            var owner = new ParallelSearcher(1024 * 1024);
            try
            {
                var (pv, _, score, nodes, _) = owner.NodeBoundSearch(state, nodeLimit: nodeLimit);
                return (pv, score, nodes);
            }
            finally
            {
                owner.Dispose();
            }
        }

        var first = Run();
        var second = Run();

        Assert.True(first.nodes == second.nodes, "node-limited single-worker search must be deterministic");
        Assert.Equal(second.pv, first.pv);
        Assert.Equal(second.score, first.score);
        Assert.True(first.nodes >= nodeLimit, "the contract stops when NodesVisited >= nodeLimit, not >");
        Assert.True(first.nodes <= nodeLimit + 1, "single-thread search checks the limit at every node, so only the boundary node may overshoot");
    }

    [Fact]
    public void TinyNodeLimit_StillReturnsLegalMove()
    {
        using var state = State();
        var owner = new ParallelSearcher(1024);
        try
        {
            var (pv, _, _, _, _) = owner.NodeBoundSearch(state, nodeLimit: 1);
            Assert.True(pv.Count > 0, "a legal fallback is required at very small limits");
            Assert.Contains(pv[0], state.LegalMoves);
        }
        finally
        {
            owner.Dispose();
        }
    }

    [Fact]
    public unsafe void InterruptedSearch_PublishesNoRootEntry()
    {
        using var state = State();
        var owner = new ParallelSearcher(1024 * 1024);
        try
        {
            var worker = owner.Searchers[0];
            owner.NodeBoundSearch(state, nodeLimit: 1);

            ref var entry = ref owner.Transpositions[state.Board.Hash & worker.TtMask];
            var snapshot = TranspositionTableExtensions.Probe(ref entry, shared: false);
            Assert.True(snapshot.FullHash != state.Board.Hash, "an interrupted iteration must not publish a root TT entry with a garbage score");
        }
        finally
        {
            owner.Dispose();
        }
    }

    [NativeThreadsFact]
    public void StopDuringSearch_ReturnsCompletedIterationResult()
    {
        using var state = State();
        var owner = new ParallelSearcher(1024 * 1024);
        try
        {
            var worker = owner.Searchers[0];
            var search = Task.Factory.StartNew(
                () => owner.DepthBoundSearch(state, 90),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            try
            {
                Assert.True(SpinWait.SpinUntil(
                    () => System.Threading.Volatile.Read(ref worker.NodesVisited) >= 100_000 || search.IsCompleted,
                    TimeSpan.FromSeconds(30)), "search did not reach 100k nodes");
                Assert.False(search.IsCompleted, "fixture must stop an active search");

                owner.Stop();
                Assert.True(search.Wait(TimeSpan.FromSeconds(5)), "stop must terminate the search promptly");

                var (pv, depth, _, _, _) = search.Result;
                Assert.NotEmpty(pv);
                Assert.Contains(pv[0], state.LegalMoves);
                Assert.True(depth >= 1, "the reported depth must come from a completed iteration");
            }
            finally
            {
                owner.Stop();
                search.GetAwaiter().GetResult();
            }
        }
        finally
        {
            owner.Dispose();
        }
    }

    [Fact]
    public void TimeBoundSearch_SmallDeadline_ReturnsLegalMoveWithBoundedOverrun()
    {
        using var state = State();
        var owner = new ParallelSearcher(1024 * 1024);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (pv, _, _, _, _) = owner.TimeBoundSearch(state, 30);
            sw.Stop();

            Assert.NotEmpty(pv);
            Assert.Contains(pv[0], state.LegalMoves);
            Assert.True(sw.ElapsedMilliseconds < 2000, "the monotonic hard deadline must be enforced inside the search, not only between iterations");
        }
        finally
        {
            owner.Dispose();
        }
    }
}
