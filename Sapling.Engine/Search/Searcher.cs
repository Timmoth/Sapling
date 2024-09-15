using Sapling.Engine.Transpositions;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Runtime.InteropServices;
using System.Text;
using FluentResults;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Search;
#if AVX512
using AvxIntrinsics = System.Runtime.Intrinsics.X86.Avx512BW;
using VectorType = System.Runtime.Intrinsics.Vector512;
using VectorInt = System.Runtime.Intrinsics.Vector512<int>;
using VectorShort = System.Runtime.Intrinsics.Vector512<short>;
#else
using AvxIntrinsics = Avx2;
using VectorType = Vector256;
using VectorInt = Vector256<int>;
using VectorShort = Vector256<short>;
#endif
public unsafe partial class Searcher
{
    public readonly uint TtMask;
    private static readonly int[] AsperationWindows = { 40, 100, 300, 900, 2700, Constants.MaxScore };
    private readonly Transposition* _transpositionTable;
    public readonly RepetitionTable RepetitionTable = new();
    public readonly Transposition[] Transpositions;
    private readonly uint* _pVTable;
    public uint BestSoFar = 0;
    private long _lockedUntil;

    private bool _searchCancelled;
    public BoardState Board = default!;
    public int NodesVisited;

    const nuint _pvTableLength = Constants.MaxSearchDepth * (Constants.MaxSearchDepth + 1) / 2;
    private const nuint _pvTableBytes = _pvTableLength * sizeof(uint);
    public static unsafe uint* AlignedAllocZeroed()
    {
        const nuint alignment = 64;
        var block = NativeMemory.AlignedAlloc(_pvTableBytes, alignment);
        if (block == null)
        {
            throw new OutOfMemoryException("Failed to allocate aligned memory.");
        }

        NativeMemory.Clear(block, _pvTableBytes);
        return (uint*)block;
    }

    public Searcher(Transposition[] transpositions)
    {
        Transpositions = transpositions;
        fixed (Transposition* p = transpositions)
        {
            _transpositionTable = p; // Store the pointer
        }

        TtMask = (uint)transpositions.Length - 1;
        _pVTable = AlignedAllocZeroed();
    }

    public void Stop()
    {
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


    public (List<uint> pv, int depthSearched, int score, int nodes) Search(int nodeLimit = 0,
        int depthLimit = 0, bool writeInfo = false)
    {
        NodesVisited = 0;
        BestSoFar = 0;

        var depthSearched = 0;
        _searchCancelled = false;

        Span<uint> killers = stackalloc uint[Constants.MaxSearchDepth * 2];
        Span<int> history = stackalloc int[13 * 64];
        Span<uint> counters = stackalloc uint[13 * 64];

        NativeMemory.Clear(_pVTable, _pvTableBytes);

        var alpha = Constants.MinScore;
        var beta = Constants.MaxScore;
        var lastIterationEval = 0;

        var maxDepth = depthLimit > 0 ? depthLimit : Constants.MaxSearchDepth;

        RepetitionTable.Init(Board.RepetitionPositionHistory);
        killers.Clear();
        var bestEval = lastIterationEval = NegaMaxSearch(killers, counters, history, 0, 0, alpha, beta, false);

        BestSoFar = _pVTable[0];
        var pvMoves = stackalloc uint[Constants.MaxSearchDepth];

        NativeMemory.Copy(_pVTable, pvMoves, (nuint)Constants.MaxSearchDepth * sizeof(uint));

        var startTime = DateTime.Now;
        for (var j = 1; j <= maxDepth; j++)
        {
            var alphaWindowIndex = 0;
            var betaWindowIndex = 0;
            do
            {
                alpha = alphaWindowIndex >= 5
                    ? Constants.MinScore
                    : lastIterationEval - AsperationWindows[alphaWindowIndex];
                beta = betaWindowIndex >= 5
                    ? Constants.MaxScore
                    : lastIterationEval + AsperationWindows[betaWindowIndex];

                RepetitionTable.Init(Board.RepetitionPositionHistory);
                killers.Clear();

                var eval = NegaMaxSearch(killers, counters, history, 0, j, alpha, beta, false);

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

                if (_searchCancelled || (nodeLimit > 0 && NodesVisited > nodeLimit))
                {
                    break;
                }
            } while (true);

            if (_pVTable[0] == 0)
            {
                break;
            }

            BestSoFar = _pVTable[0];
            NativeMemory.Copy(_pVTable, pvMoves, (nuint)Constants.MaxSearchDepth * sizeof(uint));
            depthSearched = j;
            bestEval = lastIterationEval;

            if (writeInfo)
            {
                var dt = (DateTime.Now - startTime);
                var nps = (int)(NodesVisited / dt.TotalSeconds);
                var sb = new StringBuilder();
                for (var i = 0; i < Constants.MaxSearchDepth; i++)
                {
                    if (pvMoves[i] == 0)
                    {
                        break;
                    }

                    sb.Append(" ");
                    sb.Append(pvMoves[i].ToUciMoveName());
                }

                Console.WriteLine($"info depth {depthSearched} score {ScoreToString(bestEval)} nodes {NodesVisited} nps {nps} time {(int)dt.TotalMilliseconds} pv{sb}");
            }

            if (_searchCancelled || (nodeLimit > 0 && NodesVisited > nodeLimit))
            {
                break;
            }
        }
        return (GetPvMoveList(pvMoves), depthSearched, bestEval, NodesVisited);
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

    public (List<uint> pv, int depthSearched, int score, int nodes) DepthBoundSearch(int depth)
    {
        NodesVisited = 0;
        var depthSearched = 0;
        _searchCancelled = false;
        var alpha = Constants.MinScore;
        var beta = Constants.MaxScore;
        var lastIterationEval = 0;
        BestSoFar = 0;
        Span<uint> killers = stackalloc uint[Constants.MaxSearchDepth * 2];
        Span<int> history = stackalloc int[13 * 64];
        Span<uint> counters = stackalloc uint[13 * 64];
        NativeMemory.Clear(_pVTable, _pvTableBytes);
        var pvMoves = stackalloc uint[Constants.MaxSearchDepth];

        RepetitionTable.Init(Board.RepetitionPositionHistory);
        var bestEval = lastIterationEval = NegaMaxSearch(killers, counters, history, 0, 0, alpha, beta, false);
        NativeMemory.Copy(_pVTable, pvMoves, (nuint)Constants.MaxSearchDepth * sizeof(uint));
        BestSoFar = _pVTable[0];

        for (var j = 1; j <= depth; j++)
        {
            var alphaWindowIndex = 0;
            var betaWindowIndex = 0;
            do
            {
                if (alphaWindowIndex >= 5)
                {
                    alpha = Constants.MinScore;
                }
                else
                {
                    alpha = lastIterationEval - AsperationWindows[alphaWindowIndex];
                }

                if (betaWindowIndex >= 5)
                {
                    beta = Constants.MaxScore;
                }
                else
                {
                    beta = lastIterationEval + AsperationWindows[betaWindowIndex];
                }

                RepetitionTable.Init(Board.RepetitionPositionHistory);
                var eval = NegaMaxSearch(killers, counters, history, 0, j, alpha, beta, false);

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
            } while (true);

            if (_pVTable[0] == 0)
            {
                break;
            }

            BestSoFar = _pVTable[0];
            NativeMemory.Copy(_pVTable, pvMoves, (nuint)Constants.MaxSearchDepth * sizeof(uint));
            depthSearched = j;
            bestEval = lastIterationEval;

            if (_searchCancelled)
            {
                break;
            }
        }

        return (GetPvMoveList(_pVTable), depthSearched, bestEval, NodesVisited);
    }



    public void Init(long currentUnixSeconds, BoardState board)
    {
        // Cancels active search
        _searchCancelled = true;
        Board = board;
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