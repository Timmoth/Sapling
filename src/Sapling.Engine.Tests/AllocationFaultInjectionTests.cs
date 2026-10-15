using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sapling.Engine.Search;
using Xunit;

// Fault injection uses process-global hooks, so tests run serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Sapling.Engine.Tests;

// Check allocation failures, disposal, finalizers and concurrent table changes.
// Static tables live for the process, so memory assertions use a warmed baseline.
public class AllocationFaultInjectionTests
{
    private sealed class BalanceTracker : IDisposable
    {
        private readonly HashSet<IntPtr> _outstanding = new();
        private readonly object _gate = new();
        public int DoubleFreeViolations;
        public int AllocationCount;
        public int Baseline;

        public BalanceTracker()
        {
            // Finalizers from earlier tests can free memory after the process-global
            // hooks are replaced. Drain them before this tracker starts accounting.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            MemoryHelpers.InjectFaultForTesting(null, OnAlloc, OnFree);
            WarmStaticTables();
            Baseline = Outstanding;
        }

        public void FailOnNthAllocation(int n)
        {
            MemoryHelpers.InjectFaultForTesting(_ => ++AllocationCount == n, OnAlloc, OnFree);
        }

        public void ResetCounter() => AllocationCount = 0;

        public int Outstanding
        {
            get { lock (_gate) return _outstanding.Count; }
        }

        // Force every lazily-initialized static table through the hooks before any
        // baseline is taken, so faults can only hit construction-time allocations.
        private static void WarmStaticTables()
        {
            // Explicitly force lazily-initialized statics that a shallow search may not
            // reach (e.g. HistoryHeuristicExtensions.BonusTable only initializes on the
            // first quiet beta cutoff), so no cctor allocation can land inside a
            // measured window regardless of test execution order.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(HistoryHeuristicExtensions).TypeHandle);
            using var state = GameState.InitialState();
            var owner = new ParallelSearcher(1024);
            try
            {
                owner.DepthBoundSearch(state, 8);
            }
            finally
            {
                owner.Dispose();
            }
        }

        private void OnAlloc(IntPtr p)
        {
            lock (_gate)
            {
                _outstanding.Add(p).Should().BeTrue("allocation pointer unexpectedly reused before free");
            }
        }

        private void OnFree(IntPtr p)
        {
            lock (_gate)
            {
                if (!_outstanding.Remove(p))
                    DoubleFreeViolations++;
            }
        }

        public void Dispose() => MemoryHelpers.InjectFaultForTesting(null, null, null);
    }

    [Fact]
    public unsafe void SearcherConstruction_FaultAtEveryAllocationSite_FreesAllPriorBlocks()
    {
        using var tracker = new BalanceTracker();

        // Count the allocation sites by constructing successfully once.
        tracker.FailOnNthAllocation(int.MaxValue);
        var table = MemoryHelpers.Allocate<Transpositions.Transposition>(1024);
        tracker.ResetCounter();
        new Searcher(table, 1024).Dispose();
        var sites = tracker.AllocationCount;
        sites.Should().BeGreaterThan(10);

        for (var k = 1; k <= sites; k++)
        {
            tracker.ResetCounter();
            tracker.FailOnNthAllocation(k);
            Assert.Throws<OutOfMemoryException>(() => new Searcher(table, 1024));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            tracker.Outstanding.Should().Be(tracker.Baseline + 1,
                $"construction fault {k}/{sites} must leave only the caller's table");
        }

        MemoryHelpers.FreeAligned(table); // free while hooks are still observing
        tracker.DoubleFreeViolations.Should().Be(0);
        tracker.Outstanding.Should().Be(tracker.Baseline);
    }

    [Fact]
    public unsafe void ParallelSearcherConstruction_WorkerFailure_FreesOwnedTable()
    {
        using var tracker = new BalanceTracker();

        tracker.FailOnNthAllocation(2); // table succeeds, first worker block fails
        Assert.Throws<OutOfMemoryException>(() => new ParallelSearcher(1024));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        tracker.Outstanding.Should().Be(tracker.Baseline,
            "the owned table must be released when worker construction fails");
        tracker.DoubleFreeViolations.Should().Be(0);
    }

    [Fact]
    public void DisposeThenFinalizer_EachBlockFreedExactlyOnce()
    {
        using var tracker = new BalanceTracker();
        var owner = new ParallelSearcher(1024);
        owner.SetThreads(3);
        owner.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        tracker.Outstanding.Should().Be(tracker.Baseline);
        tracker.DoubleFreeViolations.Should().Be(0);
    }

    [NativeFinalizerFact]
    public void FinalizerOnly_EachBlockFreedExactlyOnce()
    {
        using var tracker = new BalanceTracker();
        CreateAndDrop();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        tracker.Outstanding.Should().Be(tracker.Baseline,
            "child finalizers must free child buffers without the parent");
        tracker.DoubleFreeViolations.Should().Be(0, "parent finalizer must not double-free child buffers");
        return;

        static void CreateAndDrop()
        {
            var owner = new ParallelSearcher(1024);
            owner.SetThreads(3);
            _ = owner;
        }
    }

    [Fact]
    public void ResizeUpAndDown_ReturnsToAllocationBaseline()
    {
        using var tracker = new BalanceTracker();
        var owner = new ParallelSearcher(1024);
        try
        {
            // TT(1) + exactly one worker at creation.
            var perWorker = tracker.Outstanding - tracker.Baseline - 1;
            perWorker.Should().BeGreaterThan(10);
            for (var cycle = 0; cycle < 5; cycle++)
            {
                for (var threads = 2; threads <= 4; threads++)
                    owner.SetThreads(threads);
                for (var threads = 3; threads >= 1; threads--)
                    owner.SetThreads(threads);
                tracker.Outstanding.Should().Be(tracker.Baseline + 1 + owner.Searchers.Count * perWorker,
                    "resize cycles must not leak or double-free worker buffers");
            }
            tracker.DoubleFreeViolations.Should().Be(0);
        }
        finally
        {
            owner.Dispose();
        }
        tracker.Outstanding.Should().Be(tracker.Baseline);
        tracker.DoubleFreeViolations.Should().Be(0);
    }

    [NativeThreadsFact]
    public void ConcurrentStructuralChanges_SerializeAgainstActiveSearches()
    {
        using var tracker = new BalanceTracker();
        var owner = new ParallelSearcher(64 * 1024);
        using var state = GameState.InitialState();
        var stop = false;
        var completedSearches = 0;
        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var tasks = new List<Task>();

        try
        {
            for (var s = 0; s < 2; s++)
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        while (!Volatile.Read(ref stop))
                        {
                            try
                            {
                                owner.TimeBoundSearch(state, 15);
                                Interlocked.Increment(ref completedSearches);
                            }
                            catch (InvalidOperationException ex) when (ex.Message == "A structural change is pending.")
                            {
                                // A pending mutation rejects this attempt, not the entire
                                // search thread. Keep exercising searches throughout stress.
                                Thread.Yield();
                            }
                        }
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException)
                    {
                        // Documented rejections: disposed owner, or a search racing a pending
                        // structural mutation. Anything else is a defect.
                    }
                    catch (Exception ex)
                    {
                        failures.Enqueue($"search: {ex}");
                    }
                }));

            tasks.Add(Task.Run(() =>
            {
                try
                {
                    while (!Volatile.Read(ref stop))
                    {
                        owner.SetThreads(Random.Shared.Next(1, 5));
                        owner.NewGame();
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex) when (ex is ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    failures.Enqueue($"mutate: {ex}");
                }
            }));

            tasks.Add(Task.Run(() =>
            {
                while (!Volatile.Read(ref stop))
                {
                    owner.Stop();
                    Thread.Sleep(1);
                }
            }));

            Thread.Sleep(2000);
            Volatile.Write(ref stop, true);
            Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(15)).Should().BeTrue("stress tasks must finish");
            failures.Should().BeEmpty();
            completedSearches.Should().BePositive("stress must actually complete searches");

            owner.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            // state is still alive here: its HashHistory block is expected outstanding.
            tracker.Outstanding.Should().Be(tracker.Baseline + 1,
                "stress must return allocations to baseline");
            tracker.DoubleFreeViolations.Should().Be(0);
        }
        finally
        {
            Volatile.Write(ref stop, true);
            owner.Dispose();
        }
    }
}
