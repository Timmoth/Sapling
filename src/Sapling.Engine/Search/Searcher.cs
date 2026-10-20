using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher : IDisposable
{
    private const int _pvTableLength = Constants.MaxSearchDepth * (Constants.MaxSearchDepth + 1) / 2;
    private const nuint _pvTableBytes = _pvTableLength * sizeof(uint);
    private readonly uint* _pVTable;
    public readonly Transposition* Transpositions;
    public readonly ulong TtMask;
    private long _lockedUntil;
    private int _disposed;
    private readonly object _lifecycleGate = new();
    private int _buffersFreed;
    internal bool SharedTranspositions = true;
    internal object? TableOwner; // Keep the owner alive when callers retain a borrowed worker.

    private volatile bool _searchCancelled;
    // Sticky shutdown: a reset must never re-arm a search once the session is closing.
    private volatile bool _aborted;
    private long _deadlineTimestamp;
    private long _softDeadlineTimestamp;
    private int _softLimitMs;
    private int _hardLimitMs;
    private int _nodeLimit;

    // Bounded-slice controls for the resumable browser driver (4.3). Zero means "no slice
    // limit", so the native/CLI path is unaffected. Hitting a slice limit sets
    // _sliceExhausted (aborts the current iteration) rather than _searchCancelled (which
    // would end the whole search), so the search resumes on the next slice.
    private long _sliceNodeBudget;
    private long _sliceDeadlineTs;
    private long _sliceStartNodes;
    private bool _sliceExhausted;
    // True only while a resumable slice is configured. Native sessions never set it, so the
    // hot ShouldStop path skips the slice checks with a single predictable branch.
    private bool _sliceActive;

    internal void SetTimeLimit(int milliseconds) => SetTimeLimits(milliseconds, milliseconds);

    internal void SetTimeLimits(int softLimitMs, int hardLimitMs)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var frequency = System.Diagnostics.Stopwatch.Frequency;
        _softLimitMs = Math.Max(0, softLimitMs);
        _hardLimitMs = Math.Max(0, hardLimitMs);
        _softDeadlineTimestamp = softLimitMs > 0
            ? now + (long)(softLimitMs * (double)frequency / 1000)
            : 0;
        _deadlineTimestamp = hardLimitMs > 0
            ? now + (long)(hardLimitMs * (double)frequency / 1000)
            : 0;
    }

    // Single stop contract: every stop condition (explicit stop, node limit, hard deadline)
    // sets the sticky _searchCancelled flag. NodesVisited >= nodeLimit is the one boundary;
    // callers must not re-derive stop state from NodesVisited with a different comparison.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldStop()
    {
        if (_searchCancelled)
            return true;
        var nodeLimit = _nodeLimit;
        if (nodeLimit > 0 && NodesVisited >= nodeLimit)
        {
            _searchCancelled = true;
            return true;
        }

        var sliceActive = _sliceActive;
        if (sliceActive && _sliceNodeBudget > 0 && NodesVisited - _sliceStartNodes >= _sliceNodeBudget)
        {
            _sliceExhausted = true;
            return true;
        }

        // Reference path for the deferred deadline-field optimization: load the clock
        // deadline on every visited node while retaining the same stop semantics.
        var deadline = _deadlineTimestamp;
        var sliceDeadline = sliceActive ? _sliceDeadlineTs : 0;
        if (deadline != 0 || sliceDeadline != 0)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (deadline != 0 && now >= deadline)
            {
                _searchCancelled = true;
                return true;
            }
            if (sliceDeadline != 0 && now >= sliceDeadline)
            {
                _sliceExhausted = true;
                return true;
            }
        }
        return false;
    }
    // Unified "this search work is interrupted" signal for recursive return paths. It covers both
    // permanent cancellation and resumable slice exhaustion, so an interrupted child's sentinel
    // return is never consumed as a real score, and no parent TT/history/PV/correction result that
    // depends on an interrupted child is published. Native searches never set _sliceExhausted, so
    // this is identical to _searchCancelled there.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Interrupted() => _searchCancelled || _sliceExhausted;
    public uint BestSoFar;
    public long NodesVisited;
    private const int MaxMovesPerPly = 218;
    private const int KillersLength = Constants.MaxSearchDepth * 2;

    private const int HistoryLength = 13 * 64;
    private const int CountersLength = 13 * 64;
    public readonly BoardStateData* BucketCacheWhiteBoards;
    public readonly short** BucketCacheWhiteAccumulators;
    public readonly BoardStateData* BucketCacheBlackBoards;
    public readonly short** BucketCacheBlackAccumulators;
    public readonly short** WhiteAccumulators;
    public readonly short** BlackAccumulators;
    public readonly ulong* HashHistory;

    public readonly BoardStateData* Boards;
    public readonly AccumulatorState* Accumulators;

    public readonly int* History;
    internal readonly short* ThreatHistory;
    internal readonly short* ContinuationHistory;
    public readonly uint* Counters;
    public readonly uint* killers;
    public readonly int* PawnCorrHist;
    public readonly int* WhiteMaterialCorrHist;
    public readonly int* BlackMaterialCorrHist;
    public readonly int* PreviousMoveCorrHist;

    public Searcher(Transposition* transpositions, long ttCount)
    {
        if (ttCount <= 0 || !System.Numerics.BitOperations.IsPow2((ulong)ttCount))
            throw new ArgumentOutOfRangeException(nameof(ttCount));
        if (transpositions == null)
            throw new ArgumentNullException(nameof(transpositions));
        Transpositions = transpositions;

        TtMask = (ulong)(ttCount - 1);
        try
        {
        _pVTable = MemoryHelpers.Allocate<uint>(_pvTableLength);

        WhiteAccumulators = AllocateSearchStack(Constants.MaxSearchDepth + 1);
        BlackAccumulators = AllocateSearchStack(Constants.MaxSearchDepth + 1);
        for (var i = 0; i < Constants.MaxSearchDepth + 1; i++)
        {
            WhiteAccumulators[i] = AllocateAccumulator();
            BlackAccumulators[i] = AllocateAccumulator();
        }

        // Boards/Accumulators are indexed by ply; the frame at depthFromRoot = MaxSearchDepth-1
        // advances to index MaxSearchDepth before the depth guard returns, so capacity must be
        // MaxSearchDepth + 1 (off-by-one heap write otherwise).
        Boards = MemoryHelpers.Allocate<BoardStateData>(Constants.MaxSearchDepth + 1);
        Accumulators = MemoryHelpers.Allocate<AccumulatorState>(Constants.MaxSearchDepth + 1);

        BucketCacheWhiteBoards = MemoryHelpers.Allocate<BoardStateData>(NnueWeights.InputBuckets * 2);
        BucketCacheBlackBoards = MemoryHelpers.Allocate<BoardStateData>(NnueWeights.InputBuckets * 2);

        BucketCacheWhiteAccumulators = AllocateSearchStack(NnueWeights.InputBuckets * 2);
        BucketCacheBlackAccumulators = AllocateSearchStack(NnueWeights.InputBuckets * 2);
        for (var i = 0; i < NnueWeights.InputBuckets * 2; i++)
        {
            BucketCacheWhiteAccumulators[i] = AllocateAccumulator();
            BucketCacheBlackAccumulators[i] = AllocateAccumulator();
        }

        HashHistory = MemoryHelpers.Allocate<ulong>(GameState.HashHistoryLength);
        Counters = MemoryHelpers.Allocate<uint>(CountersLength);
        History = MemoryHelpers.Allocate<int>(HistoryLength);
        ThreatHistory = MemoryHelpers.Allocate<short>(ThreatHistoryLength);
        ContinuationHistory = MemoryHelpers.Allocate<short>(ContinuationHistoryLength);
        killers = MemoryHelpers.Allocate<uint>(KillersLength);
        PawnCorrHist = MemoryHelpers.Allocate<int>(TableElementsSize);
        WhiteMaterialCorrHist = MemoryHelpers.Allocate<int>(TableElementsSize);
        BlackMaterialCorrHist = MemoryHelpers.Allocate<int>(TableElementsSize);
        PreviousMoveCorrHist = MemoryHelpers.Allocate<int>(CountersLength);
        }
        catch
        {
            FreeBuffers();
            GC.SuppressFinalize(this);
            throw;
        }
    }

    public static short* AllocateAccumulator() =>
        (short*)MemoryHelpers.Allocate<short>(NnueWeights.Layer1Size);

    public static short** AllocateSearchStack(nuint items) =>
        (short**)MemoryHelpers.Allocate<nint>(checked((long)items));

    ~Searcher()
    {
        FreeBuffers();
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Stop();
        lock (_lifecycleGate)
        {
            FreeBuffers();
        }
        GC.SuppressFinalize(this);
    }

    private void FreeBuffers()
    {
        if (Interlocked.Exchange(ref _buffersFreed, 1) != 0)
            return;
        if (_pVTable != null)
            MemoryHelpers.FreeAligned(_pVTable);

        if (HashHistory != null)
            MemoryHelpers.FreeAligned(HashHistory);

        if (Counters != null)
            MemoryHelpers.FreeAligned(Counters);

        if (History != null)
            MemoryHelpers.FreeAligned(History);

        if (ThreatHistory != null)
            MemoryHelpers.FreeAligned(ThreatHistory);

        if (ContinuationHistory != null)
            MemoryHelpers.FreeAligned(ContinuationHistory);

        if (killers != null)
            MemoryHelpers.FreeAligned(killers);

        if (PawnCorrHist != null)
            MemoryHelpers.FreeAligned(PawnCorrHist);

        if (WhiteMaterialCorrHist != null)
            MemoryHelpers.FreeAligned(WhiteMaterialCorrHist);

        if (BlackMaterialCorrHist != null)
            MemoryHelpers.FreeAligned(BlackMaterialCorrHist);

        if (PreviousMoveCorrHist != null)
            MemoryHelpers.FreeAligned(PreviousMoveCorrHist);

        for (var i = 0; i < Constants.MaxSearchDepth + 1; i++)
        {
            if (WhiteAccumulators != null && WhiteAccumulators[i] != null)
                MemoryHelpers.FreeAligned(WhiteAccumulators[i]);

            if (BlackAccumulators != null && BlackAccumulators[i] != null)
                MemoryHelpers.FreeAligned(BlackAccumulators[i]);
        }

        if (WhiteAccumulators != null)
            MemoryHelpers.FreeAligned(WhiteAccumulators);

        if (BlackAccumulators != null)
            MemoryHelpers.FreeAligned(BlackAccumulators);

        for (var i = 0; i < NnueWeights.InputBuckets * 2; i++)
        {
            if (BucketCacheWhiteAccumulators != null && BucketCacheWhiteAccumulators[i] != null)
                MemoryHelpers.FreeAligned(BucketCacheWhiteAccumulators[i]);

            if (BucketCacheBlackAccumulators != null && BucketCacheBlackAccumulators[i] != null)
                MemoryHelpers.FreeAligned(BucketCacheBlackAccumulators[i]);
        }

        if (BucketCacheWhiteAccumulators != null)
            MemoryHelpers.FreeAligned(BucketCacheWhiteAccumulators);

        if (BucketCacheBlackAccumulators != null)
            MemoryHelpers.FreeAligned(BucketCacheBlackAccumulators);

        if (BucketCacheWhiteBoards != null)
            MemoryHelpers.FreeAligned(BucketCacheWhiteBoards);

        if (BucketCacheBlackBoards != null)
            MemoryHelpers.FreeAligned(BucketCacheBlackBoards);

        if (Boards != null)
            MemoryHelpers.FreeAligned(Boards);

        if (Accumulators != null)
            MemoryHelpers.FreeAligned(Accumulators);
    }

    public void Stop()
    {
        _searchCancelled = true;
    }

    /// <summary>Stops the current search and every search started afterwards.</summary>
    public void Abort()
    {
        _aborted = true;
        _searchCancelled = true;
    }

    public List<uint> GetPvMoveList(uint* moves)
    {
        var moveList = new List<uint>();

        for (var i = 0; i < Constants.MaxSearchDepth; i++)
        {
            if (moves[i] == 0)
            {
                break;
            }

            moveList.Add(moves[i]);
        }

        return moveList;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetAsperationWindow(int index)
    {
        return index switch
        {
            0 => SpsaOptions.AsperationWindowA,
            1 => SpsaOptions.AsperationWindowB,
            2 => SpsaOptions.AsperationWindowC,
            3 => SpsaOptions.AsperationWindowD,
            4 => SpsaOptions.AsperationWindowE,
            _ => Constants.MaxScore
        };
    }

    public void Reset(GameState inputBoard)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            ResetCore(inputBoard);
        }
    }

    private void ResetCore(GameState inputBoard)
    {
        inputBoard.EnsureUsable();
        _searchCancelled = _aborted;
        _deadlineTimestamp = 0;
        _softDeadlineTimestamp = 0;
        _softLimitMs = 0;
        _hardLimitMs = 0;
        _nodeLimit = 0;
        // Clear resumable slice state so a stale _sliceExhausted cannot make Interrupted() true
        // for a subsequent native search on the same worker.
        _sliceActive = false;
        _sliceExhausted = false;
        _sliceNodeBudget = 0;
        _sliceDeadlineTs = 0;
        _sliceStartNodes = 0;
        NodesVisited = 0;
        BestSoFar = 0;

        // Carry a fading quiet-move ordering signal across successive search calls.
        for (var i = 0; i < HistoryLength; i++)
            History[i] = History[i] * 3 / 4;
        NativeMemory.Clear(Counters, (nuint)CountersLength * sizeof(uint));
        NativeMemory.Clear(killers, (nuint)KillersLength * sizeof(uint));

        NativeMemory.Clear(_pVTable, _pvTableBytes);

        NativeMemory.Clear(Boards, (nuint)sizeof(BoardStateData) * (Constants.MaxSearchDepth + 1));
        NativeMemory.Clear(Accumulators, (nuint)sizeof(AccumulatorState) * (Constants.MaxSearchDepth + 1));

        NativeMemory.Clear(BucketCacheWhiteBoards, (nuint)sizeof(BoardStateData) * NnueWeights.InputBuckets * 2);
        NativeMemory.Clear(BucketCacheBlackBoards, (nuint)sizeof(BoardStateData) * NnueWeights.InputBuckets * 2);

        var historyToCopy = Math.Min((uint)inputBoard.Board.TurnCount, (uint)GameState.HashHistoryLength);
        Unsafe.CopyBlock(HashHistory, inputBoard.HashHistory, sizeof(ulong) * historyToCopy);
    }

    public unsafe void ClearMoveHistory()
    {
        NativeMemory.Clear(History, (nuint)HistoryLength * sizeof(int));
        NativeMemory.Clear(ThreatHistory, (nuint)ThreatHistoryLength * sizeof(short));
        NativeMemory.Clear(ContinuationHistory, (nuint)ContinuationHistoryLength * sizeof(short));
    }

    public (List<uint> pv, int depthSearched, int score, long nodes) Search(GameState inputBoard, Action cancelSearch, List<Searcher>? searchers = null, int nodeLimit = 0,
        int depthLimit = 0, int threadId = -1, DateTime? timeLimit = null)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (depthLimit < 0 || depthLimit > Constants.MaxSearchDepth)
                throw new ArgumentOutOfRangeException(nameof(depthLimit));
            if (nodeLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(nodeLimit));
            inputBoard.EnsureUsable();
            _nodeLimit = nodeLimit;
            try
            {
                return SearchCore(inputBoard, cancelSearch, searchers, nodeLimit, depthLimit, threadId, timeLimit);
            }
            finally
            {
                GC.KeepAlive(inputBoard);
                GC.KeepAlive(TableOwner);
                GC.KeepAlive(this);
            }
        }
    }

    private (List<uint> pv, int depthSearched, int score, long nodes) SearchCore(
        GameState inputBoard, Action cancelSearch, List<Searcher>? searchers, int nodeLimit,
        int depthLimit, int threadId, DateTime? timeLimit)
    {
        var depthSearched = 0;

        var alpha = Constants.MinScore;
        var beta = Constants.MaxScore;
        var lastIterationEval = 0;

        var maxDepth = depthLimit > 0 ? depthLimit : Constants.MaxSearchDepth;

        // The DateTime parameter is retained for direct callers that do not set a deadline.
        if (_deadlineTimestamp == 0 && timeLimit.HasValue)
        {
            var remainingMs = (timeLimit.Value - DateTime.Now).TotalMilliseconds;
            var limitMs = Math.Clamp((int)Math.Ceiling(remainingMs), 1, int.MaxValue);
            _deadlineTimestamp = remainingMs > 0
                ? System.Diagnostics.Stopwatch.GetTimestamp() + (long)(limitMs * (double)System.Diagnostics.Stopwatch.Frequency / 1000)
                : System.Diagnostics.Stopwatch.GetTimestamp();
            _softDeadlineTimestamp = _deadlineTimestamp;
            _softLimitMs = _hardLimitMs = remainingMs > 0 ? limitMs : 0;
        }

        ref var rootBoard = ref Boards[0];

        inputBoard.Board.CloneTo(ref rootBoard);
        FillInitialAccumulators(Boards, Accumulators);

        var bestEval = lastIterationEval = NegaMaxSearch(Boards, Accumulators, 0, 0, alpha, beta);

        BestSoFar = _pVTable[0];
        // One slot past the deepest iteration so the copied PV always has a terminator in range.
        var pvMoves = stackalloc uint[Constants.MaxSearchDepth + 1];

        NativeMemory.Copy(_pVTable, pvMoves, (nuint)Constants.MaxSearchDepth * sizeof(uint));
        pvMoves[Constants.MaxSearchDepth] = 0;

        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var previousBestMove = BestSoFar;
        var previousScore = bestEval;
        var stableBestMoves = 0;
        // Iteration j searches to depth j, so a 'go depth N' limit must include j == maxDepth.
        for (var j = 1; j <= maxDepth; j++)
        {
            var iterationStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

            if (_searchCancelled)
            {
                break;
            }

            var alphaWindowIndex = 0;
            var betaWindowIndex = 0;
            do
            {
                if (j <= 4)
                {
                    alpha = Constants.MinScore;
                    beta = Constants.MaxScore;
                    lastIterationEval = NegaMaxSearch(Boards, Accumulators, 0, j, alpha, beta);
                    break;
                }

                alpha = lastIterationEval - GetAsperationWindow(alphaWindowIndex);
                beta = lastIterationEval + GetAsperationWindow(betaWindowIndex);

                var eval = NegaMaxSearch(Boards, Accumulators, 0, j, alpha, beta);

                if (eval <= alpha)
                {
                    ++alphaWindowIndex;
                }
                else if (eval >= beta)
                {
                    ++betaWindowIndex;
                }
                else
                {
                    lastIterationEval = eval;
                    break;
                }

                if (_searchCancelled)
                {
                    break;
                }

            } while (true);

            // Only completed iterations may replace the reported result. An iteration that
            // hit the stop contract (stop, node limit >=, or hard deadline) is discarded in
            // full: its partial PV, score and depth must never become the final answer.
            if (_searchCancelled)
            {
                break;
            }

            if (_pVTable[0] == 0)
            {
                break;
            }

            BestSoFar = _pVTable[0];
            var pvLength = Math.Min(j, Constants.MaxSearchDepth);
            NativeMemory.Copy(_pVTable, pvMoves, (nuint)pvLength * sizeof(uint));
            pvMoves[pvLength] = 0;
            depthSearched = j;
            bestEval = lastIterationEval;

            if (threadId == 0)
            {
                var nodes = searchers?.Sum(s =>s.NodesVisited) ?? NodesVisited;

                var dt = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp);
                var nps = (long)(nodes / dt.TotalSeconds);
                var sb = new StringBuilder();
                for (var i = 0; i < pvLength; i++)
                {
                    if (pvMoves[i] == 0)
                    {
                        break;
                    }

                    sb.Append(" ");
                    sb.Append(pvMoves[i].ToUciMoveName());
                }

                Console.WriteLine(
                     $"info depth {depthSearched} score {ScoreToString(bestEval)} nodes {nodes} nps {nps} time {(int)dt.TotalMilliseconds} pv{sb}");

                var iterationDuration = System.Diagnostics.Stopwatch.GetElapsedTime(iterationStartTimestamp);
                if (_softDeadlineTimestamp != 0)
                {
                    stableBestMoves = BestSoFar == previousBestMove ? stableBestMoves + 1 : 0;
                    var scoreDrop = previousScore - bestEval;
                    var adjustedSoftLimit = TimeManagement.AdjustSoftLimit(
                        _softLimitMs, _hardLimitMs, depthSearched, stableBestMoves,
                        scoreDrop);
                    var adjustedDeadline = startTimestamp + (long)(adjustedSoftLimit *
                        (double)System.Diagnostics.Stopwatch.Frequency / 1000);
                    _softDeadlineTimestamp = _deadlineTimestamp == 0
                        ? adjustedDeadline
                        : Math.Min(adjustedDeadline, _deadlineTimestamp);
                    previousBestMove = BestSoFar;
                    previousScore = bestEval;

                    if (System.Diagnostics.Stopwatch.GetTimestamp() >= _softDeadlineTimestamp)
                    {
                        cancelSearch();
                        break;
                    }
                }

                if (_deadlineTimestamp != 0)
                {
                    var timeRemaining = System.Diagnostics.Stopwatch.GetElapsedTime(System.Diagnostics.Stopwatch.GetTimestamp(), _deadlineTimestamp);
                    if (timeRemaining.TotalMilliseconds < iterationDuration.TotalMilliseconds * 1.5)
                    {
                        cancelSearch();
                        break;
                    }
                }
            }

            if (_searchCancelled)
                break;
        }

        var pvList = GetPvMoveList(pvMoves);

        // Legal fallback at very small limits: if no iteration completed, prefer the depth-0
        // root move, then any legal move. An empty list is returned only for a genuinely
        // moveless position (checkmate/stalemate).
        if (pvList.Count == 0 && BestSoFar != 0)
        {
            pvList.Add(BestSoFar);
        }
        else if (pvList.Count == 0 && inputBoard.LegalMoves.Count > 0)
        {
            pvList.Add(inputBoard.LegalMoves[0]);
        }

        return (pvList, depthSearched, bestEval, NodesVisited);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreTransposition(ref Transposition entry, ulong hash, byte depth, int ply,
        int eval, TranspositionTableFlag flag, uint move = default) =>
        TranspositionTableExtensions.Set(ref entry, hash, depth, ply, eval, flag, move, SharedTranspositions);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PrefetchTransposition(void* entry)
    {
        if (System.Runtime.Intrinsics.X86.Sse.IsSupported)
            System.Runtime.Intrinsics.X86.Sse.Prefetch0(entry);
    }

    private static string ScoreToString(int score)
    {
        if (MoveScoring.IsMateScore(score))
        {
            var sign = Math.Sign(score);
            var moves = MoveScoring.GetMateDistance(score);
            return $"mate {sign * moves}";
        }

        return $"cp {score}";
    }

    public void Init(long currentUnixSeconds)
    {
        // Cancels active search
        _searchCancelled = true;
        _lockedUntil = currentUnixSeconds + 60;
    }

    public void Release()
    {
        _lockedUntil = 0;
    }

    public bool IsBusy(long currentUnixSeconds)
    {
        return _lockedUntil > currentUnixSeconds;
    }
}
