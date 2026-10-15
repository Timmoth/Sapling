using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sapling.Engine.Search;

/// <summary>
/// Browser search split into bounded slices. Search state persists between calls;
/// interrupted iterations restart from the root with a warm transposition table.
/// </summary>
public unsafe partial class Searcher
{
    private bool _rsActive;
    private uint[]? _rsPv;
    private int _rsDepthSearched;
    private int _rsLastEval;
    private int _rsBestEval;
    private uint _rsBestMove;

    public readonly record struct ResumableResult(
        int Depth, int Score, uint BestMove, List<uint> Pv, long TotalNodes, bool Done, bool Stopped);

    /// <summary>One-time setup for a resumable search of <paramref name="state"/>.</summary>
    public void BeginResumable(GameState state)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            ResetCore(state);
            _nodeLimit = 0;
            _deadlineTimestamp = 0;
            _sliceNodeBudget = 0;
            _sliceDeadlineTs = 0;
            _sliceStartNodes = 0;
            _sliceExhausted = false;
            _sliceActive = false;

            ref var rootBoard = ref Boards[0];
            state.Board.CloneTo(ref rootBoard);
            FillInitialAccumulators(Boards, Accumulators);

            _rsBestEval = _rsLastEval = NegaMaxSearch(Boards, Accumulators, 0, 0, Constants.MinScore, Constants.MaxScore);
            _rsBestMove = _pVTable[0];
            _rsPv ??= new uint[Constants.MaxSearchDepth];
            fixed (uint* dst = _rsPv)
                NativeMemory.Copy(_pVTable, dst, (nuint)Constants.MaxSearchDepth * sizeof(uint));
            _rsDepthSearched = 0;
            _rsActive = true;
        }
    }

    /// <summary>
    /// Runs one bounded slice of the active resumable search. Returns the best completed
    /// result so far. <paramref name="sliceNodeBudget"/> and <paramref name="sliceMs"/>
    /// bound the slice; either may be zero (unbounded) but at least one should be set.
    /// </summary>
    public ResumableResult ResumableSlice(int maxDepth, long sliceNodeBudget, int sliceMs)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (!_rsActive)
                throw new InvalidOperationException("No resumable search is active.");

            try
            {
            if (!_searchCancelled)
            {
                _sliceNodeBudget = sliceNodeBudget > 0 ? sliceNodeBudget : 0;
                _sliceDeadlineTs = sliceMs > 0
                    ? Stopwatch.GetTimestamp() + (long)(sliceMs * (double)Stopwatch.Frequency / 1000)
                    : 0;
                _sliceStartNodes = NodesVisited;
                _sliceExhausted = false;
                _sliceActive = true;

                var limit = Math.Min(maxDepth, Constants.MaxSearchDepth);
                for (var j = _rsDepthSearched + 1; j <= limit; j++)
                {
                    if (_searchCancelled)
                        break;

                    var alphaWindowIndex = 0;
                    var betaWindowIndex = 0;
                    do
                    {
                        var alpha = _rsLastEval - GetAsperationWindow(alphaWindowIndex);
                        var beta = _rsLastEval + GetAsperationWindow(betaWindowIndex);
                        var eval = NegaMaxSearch(Boards, Accumulators, 0, j, alpha, beta);

                        // Check interruption before consuming the result: an interrupted iteration
                        // returns a sentinel that must not move the aspiration center or widen the
                        // window. The whole iteration is discarded and the depth is resumed next slice.
                        if (_searchCancelled || _sliceExhausted)
                            break;

                        if (eval <= alpha)
                            ++alphaWindowIndex;
                        else if (eval >= beta)
                            ++betaWindowIndex;
                        else
                        {
                            _rsLastEval = eval;
                            break;
                        }
                    } while (true);

                    // An interrupted iteration is discarded in full; resume the same depth
                    // next slice with the transposition table warm.
                    if (_searchCancelled || _sliceExhausted)
                        break;
                    if (_pVTable[0] == 0)
                        break;

                    _rsBestMove = _pVTable[0];
                    fixed (uint* dst = _rsPv)
                        NativeMemory.Copy(_pVTable, dst, (nuint)j * sizeof(uint));
                    _rsDepthSearched = j;
                    _rsBestEval = _rsLastEval;

                    if (_searchCancelled || _sliceExhausted)
                        break;
                    if (_sliceNodeBudget > 0 && NodesVisited - _sliceStartNodes >= _sliceNodeBudget)
                    {
                        _sliceExhausted = true;
                        break;
                    }
                    if (_sliceDeadlineTs != 0 && Stopwatch.GetTimestamp() >= _sliceDeadlineTs)
                    {
                        _sliceExhausted = true;
                        break;
                    }
                }
            }
            }
            finally
            {
                // Slice limits must be cleared even if the search threw, otherwise a stale
                // _sliceActive would corrupt ShouldStop() for the next operation.
                _sliceNodeBudget = 0;
                _sliceDeadlineTs = 0;
                _sliceActive = false;
            }

            var done = _rsDepthSearched >= Math.Min(maxDepth, Constants.MaxSearchDepth) || _searchCancelled;
            return Finish(done, _searchCancelled);
        }
    }

    public void EndResumable()
    {
        lock (_lifecycleGate)
        {
            _rsActive = false;
            _sliceNodeBudget = 0;
            _sliceDeadlineTs = 0;
            _sliceExhausted = false;
            _sliceActive = false;
        }
    }

    private ResumableResult Finish(bool done, bool stopped)
    {
        List<uint> pv;
        if (_rsPv is null)
            pv = new List<uint>();
        else
            fixed (uint* p = _rsPv)
                pv = GetPvMoveList(p);
        return new ResumableResult(_rsDepthSearched, _rsBestEval, _rsBestMove, pv, NodesVisited, done, stopped);
    }
}
