using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Search;

/// <summary>Owns workers and their shared table. Searches and structural changes are serialized.</summary>
public unsafe class ParallelSearcher : IDisposable
{
    private readonly List<Searcher> _searchers = new();
    public IReadOnlyList<Searcher> Searchers { get; }
    public readonly Transposition* Transpositions;
    public readonly long TTSize;
    private readonly object _operationGate = new();
    private readonly object _controlGate = new();
    private Searcher[]? _activeWorkers;
    private int _pendingMutations;
    private bool _disposed;
    private bool _aborted;
    private int _tableFreed;

    public ParallelSearcher(long ttSize)
    {
        Searchers = _searchers.AsReadOnly();
        if (ttSize <= 0 || !System.Numerics.BitOperations.IsPow2((ulong)ttSize))
            throw new ArgumentOutOfRangeException(nameof(ttSize), "Table entries must be a positive power of two.");
        TTSize = ttSize;
        Transpositions = MemoryHelpers.AllocateMultiThreaded<Transposition>(ttSize);
        try
        {
            _searchers.Add(new Searcher(Transpositions, ttSize) { TableOwner = this });
        }
        catch
        {
            FreeTable();
            GC.SuppressFinalize(this);
            throw;
        }
    }

    // Child finalizers own child buffers. Never invoke managed child cleanup from here.
    ~ParallelSearcher() => FreeTable();

    private void FreeTable()
    {
        if (Interlocked.Exchange(ref _tableFreed, 1) == 0)
            MemoryHelpers.FreeAligned(Transpositions);
    }

    public void Dispose()
    {
        lock (_controlGate)
        {
            _disposed = true;
            StopActiveWorkers();
        }
        // Also wait for another concurrent Dispose to finish before returning.
        lock (_operationGate)
        {
            foreach (var searcher in _searchers)
                searcher.Dispose();
            _searchers.Clear();
            FreeTable();
        }
        GC.SuppressFinalize(this);
    }

    public void Stop()
    {
        lock (_controlGate)
            StopActiveWorkers();
    }

    /// <summary>
    /// Stops the running search and refuses every later one. Unlike Stop this survives the
    /// worker reset a queued search performs, so a quit can never be outraced by a pending go.
    /// </summary>
    public void Abort()
    {
        lock (_controlGate)
        {
            _aborted = true;
            if (_activeWorkers is { } workers)
                foreach (var worker in workers)
                    worker.Abort();
        }
    }

    private void StopActiveWorkers()
    {
        if (_activeWorkers is { } workers)
            foreach (var worker in workers)
                worker.Stop();
    }

    public void CancelSearch() => Stop();

    private void Mutate(Action action)
    {
        lock (_controlGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pendingMutations++;
            StopActiveWorkers();
        }
        try
        {
            lock (_operationGate)
            {
                lock (_controlGate)
                    ObjectDisposedException.ThrowIf(_disposed, this);
                action();
            }
        }
        finally
        {
            lock (_controlGate)
                _pendingMutations--;
        }
    }

    public void NewGame() => Mutate(() =>
    {
        foreach (var searcher in _searchers)
        {
            searcher.ClearCorrectionHistory();
            searcher.ClearMoveHistory();
        }
    });

    public void SetThreads(int searchThreads) => Mutate(() =>
    {
        searchThreads = Math.Clamp(searchThreads, 1, OperatingSystem.IsBrowser() ? 1 : Environment.ProcessorCount);
        while (_searchers.Count > searchThreads)
        {
            var last = _searchers[^1];
            _searchers.RemoveAt(_searchers.Count - 1);
            last.Dispose();
        }
        while (_searchers.Count < searchThreads)
            _searchers.Add(new Searcher(Transpositions, TTSize) { TableOwner = this });
    });

    public (List<uint> pv, int depthSearched, int score, long nodes, TimeSpan duration) NodeBoundSearch(
        GameState state, int nodeLimit = 0, int maxDepth = 0) =>
        Execute(state, maxDepth, nodeLimit, 0, 0, true, false);

    public (List<uint> pv, int depthSearched, int score, long nodes, TimeSpan duration) TimeBoundSearch(
        GameState state, int thinkTime) => TimeBoundSearch(state, thinkTime, thinkTime);

    public (List<uint> pv, int depthSearched, int score, long nodes, TimeSpan duration) TimeBoundSearch(
        GameState state, int softLimitMs, int hardLimitMs) => Execute(state, 0, 0, softLimitMs, hardLimitMs, false, true);

    public (List<uint> move, int depthSearched, int score, long nodes, TimeSpan duration) DepthBoundSearch(
        GameState state, int depth) => Execute(state, depth, 0, 0, 0, false, false);

    private (List<uint> pv, int depthSearched, int score, long nodes, TimeSpan duration) Execute(
        GameState state, int depth, int nodeLimit, int softLimitMs, int hardLimitMs,
        bool firstWorkerOnly, bool reportProgress)
    {
        if (depth < 0 || depth > Constants.MaxSearchDepth)
            throw new ArgumentOutOfRangeException(nameof(depth));
        if (nodeLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(nodeLimit));
        lock (_operationGate)
        {
            Searcher[] workers;
            lock (_controlGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_pendingMutations != 0)
                    throw new InvalidOperationException("A structural change is pending.");
                if (_aborted)
                    throw new InvalidOperationException("Search refused: session aborted.");
                workers = firstWorkerOnly ? [_searchers[0]] : _searchers.ToArray();
                foreach (var worker in workers)
                {
                    worker.Reset(state);
                    worker.SharedTranspositions = workers.Length > 1;
                    worker.SetTimeLimits(softLimitMs, hardLimitMs);
                }
                // Stop cannot be lost between resetting worker flags and publishing the search.
                _activeWorkers = workers;
            }
            var started = Stopwatch.GetTimestamp();
            var results = new (List<uint> pv, int depth, int score, long nodes)[workers.Length];
            try
            {
                void Run(int i)
                {
                    try
                    {
                        results[i] = workers[i].Search(state, Stop, reportProgress ? _searchers : null,
                            nodeLimit: nodeLimit, depthLimit: depth, threadId: reportProgress ? i : -1,
                            timeLimit: hardLimitMs > 0 ? DateTime.Now.AddMilliseconds(hardLimitMs) : null);
                    }
                    catch
                    {
                        Stop();
                        throw;
                    }
                }
                if (workers.Length == 1)
                    Run(0); // No Task.Run, ThreadLocal or blocking task wait on the browser path.
                else
                    Parallel.For(0, workers.Length, Run); // Joins all workers, including on failure.
                var result = SelectResult(results);
                return (result.pv, result.depth, result.score, result.nodes, Stopwatch.GetElapsedTime(started));
            }
            finally
            {
                lock (_controlGate)
                    _activeWorkers = null;
                GC.KeepAlive(state);
                GC.KeepAlive(this); // The table owner must outlive all borrowed-pointer accesses.
            }
        }
    }

    // Resumable browser driver: single worker with direct table access,
    // driven in bounded slices. Ownership discipline mirrors Execute: the worker is
    // published as active so Stop() reaches it, and the operation gate serialises against
    // structural changes.
    public void BeginResumableSearch(GameState state)
    {
        lock (_operationGate)
        {
            Searcher worker;
            lock (_controlGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_pendingMutations != 0)
                    throw new InvalidOperationException("A structural change is pending.");
                if (_aborted)
                    throw new InvalidOperationException("Search refused: session aborted.");
                worker = _searchers[0];
                worker.SharedTranspositions = false; // single-worker direct access
                _activeWorkers = new[] { worker };
            }
            try
            {
                worker.BeginResumable(state);
            }
            catch
            {
                // A failed begin must not leave a worker published as active.
                lock (_controlGate)
                    _activeWorkers = null;
                throw;
            }
        }
    }

    public Searcher.ResumableResult ResumableSlice(int maxDepth, long sliceNodeBudget, int sliceMs)
    {
        lock (_operationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _searchers[0].ResumableSlice(maxDepth, sliceNodeBudget, sliceMs);
        }
    }

    /// <summary>
    /// Root ranking for human-like difficulty. Single worker, direct table access. Only reached
    /// when HumanPlay is enabled with a loaded distribution; the normal search paths are unaffected.
    /// </summary>
    public List<(uint Move, int Score, int Feat)> RankRootMoves(GameState state, int depth)
    {
        lock (_operationGate)
        {
            Searcher worker;
            lock (_controlGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_pendingMutations != 0)
                    throw new InvalidOperationException("A structural change is pending.");
                if (_aborted)
                    throw new InvalidOperationException("Search refused: session aborted.");
                worker = _searchers[0];
                worker.SharedTranspositions = false;
                _activeWorkers = new[] { worker };
            }
            try
            {
                return worker.RankRootMoves(state, depth);
            }
            finally
            {
                lock (_controlGate)
                    _activeWorkers = null;
                GC.KeepAlive(state);
                GC.KeepAlive(this);
            }
        }
    }

    public void EndResumableSearch()
    {
        lock (_operationGate)
        {
            if (_disposed)
                return;
            _searchers[0].EndResumable();
            lock (_controlGate)
                _activeWorkers = null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MoveFromToIndex(uint move) => (int)((((move >> 4) & 0x3F) * 64) + ((move >> 10) & 0x3F));

    public static int ThreadValue(int score, int worstScore, int depth) => (score - worstScore) * depth;

    private static (List<uint> pv, int depth, int score, long nodes) SelectResult(
        (List<uint> pv, int depth, int score, long nodes)[] results)
    {
        if (results.Length == 1)
            return results[0];
        var totalNodes = results.Sum(r => r.nodes);
        var candidates = results.Where(r => r.pv.Count != 0).ToArray();
        if (candidates.Length == 0)
            return (results[0].pv, results[0].depth, results[0].score, totalNodes);
        var worstScore = candidates.Min(r => r.score);
        Span<int> votes = stackalloc int[64 * 64];
        votes.Clear();
        foreach (var result in candidates)
            votes[MoveFromToIndex(result.pv[0])] += ThreadValue(result.score, worstScore, result.depth);
        var best = candidates[0];
        foreach (var candidate in candidates)
            if (votes[MoveFromToIndex(candidate.pv[0])] > votes[MoveFromToIndex(best.pv[0])])
                best = candidate;
        return (best.pv, best.depth, best.score, totalNodes);
    }
}
