using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    private const nuint _pvTableLength = Constants.MaxSearchDepth * (Constants.MaxSearchDepth + 1) / 2;
    private const nuint _pvTableBytes = _pvTableLength * sizeof(uint);
    private static readonly int[] AsperationWindows = { 40, 100, 300, 900, 2700, Constants.MaxScore };
    private readonly uint* _pVTable;
    public readonly Transposition* Transpositions;
    public readonly uint TtMask;
    private long _lockedUntil;

    private bool _searchCancelled;
    public uint BestSoFar;
    public int NodesVisited;

    public Searcher(Transposition* transpositions, int ttCount)
    {
        Transpositions = transpositions;

        TtMask = (uint)ttCount - 1;
        _pVTable = AlignedAllocZeroed();

        SearchStack = AllocateSearchStack(Constants.MaxSearchDepth + 1);
        for (var i = 0; i < Constants.MaxSearchDepth + 1; i++)
        {
            SearchStack[i] = new BoardStateEntry();
        }

        MoveStack = AllocateUlong(800);
        killers = AllocateUInt((nuint)killersLength);
        counters = AllocateUInt((nuint)countersLength);
        history = AllocateInt((nuint)historyLength);
    }
    public static unsafe BoardStateEntry* AllocateSearchStack(nuint items)
    {
        const nuint alignment = 64;

        nuint bytes = ((nuint)sizeof(BoardStateEntry) * (nuint)items);
        void* block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (BoardStateEntry*)block;
    }
    public static ulong* AllocateUlong(nuint count)
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)sizeof(ulong) * count, alignment);
        NativeMemory.Clear(block, (nuint)sizeof(ulong) * count);

        return (ulong*)block;
    }

    public static uint* AllocateUInt(nuint count)
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)sizeof(uint) * count, alignment);
        NativeMemory.Clear(block, (nuint)sizeof(uint) * count);

        return (uint*)block;
    }
    public static int* AllocateInt(nuint count)
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)sizeof(int) * count, alignment);
        NativeMemory.Clear(block, (nuint)sizeof(int) * count);

        return (int*)block;
    }

    ~Searcher()
    {
        NativeMemory.AlignedFree(MoveStack);
        NativeMemory.AlignedFree(killers);
        NativeMemory.AlignedFree(counters);
        NativeMemory.AlignedFree(history);
        for (var i = 0; i < Constants.MaxSearchDepth + 1; i++)
        {
            ref var entry = ref SearchStack[i];
            entry.Dispose();
        }
        NativeMemory.AlignedFree(SearchStack);
    }

    public static uint* AlignedAllocZeroed()
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

    private const int killersLength = Constants.MaxSearchDepth * 2;
    private const int historyLength = 13 * 64;
    private const int countersLength = 13 * 64;

    public readonly BoardStateEntry* SearchStack;
    public readonly ulong* MoveStack;

    public readonly uint* killers;
    public readonly int* history;
    public readonly uint* counters;
    public (List<uint> pv, int depthSearched, int score, int nodes) Search(GameState inputBoard, int nodeLimit = 0,
        int depthLimit = 0, bool writeInfo = false)
    {
        NodesVisited = 0;
        BestSoFar = 0;

        var depthSearched = 0;
        _searchCancelled = false;

        NativeMemory.Clear(history, (nuint)historyLength);
        NativeMemory.Clear(killers, (nuint)killersLength);
        NativeMemory.Clear(counters, (nuint)countersLength);
        NativeMemory.Clear(_pVTable, _pvTableBytes);
        Unsafe.CopyBlock(MoveStack, inputBoard.Moves, sizeof(ulong) * 800);

        var alpha = Constants.MinScore;
        var beta = Constants.MaxScore;
        var lastIterationEval = 0;

        var maxDepth = depthLimit > 0 ? depthLimit : Constants.MaxSearchDepth;

        ref var rootBoard = ref SearchStack[0];
        inputBoard.Board.CloneTo(ref rootBoard.Data);
        rootBoard.Data.FillAccumulators(ref rootBoard.AccumulatorState, rootBoard.WhiteAccumulator, rootBoard.BlackAccumulator);

        var bestEval = lastIterationEval = NegaMaxSearch(0, 0, alpha, beta, false);

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

                NativeMemory.Clear(killers, (nuint)killersLength);
                NativeMemory.Clear(counters, (nuint)countersLength);

                var eval = NegaMaxSearch(0, j, alpha, beta, false);

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
            NativeMemory.Copy(_pVTable, pvMoves, (nuint)j * sizeof(uint));
            depthSearched = j;
            bestEval = lastIterationEval;

            if (writeInfo)
            {
                var dt = DateTime.Now - startTime;
                var nps = (int)(NodesVisited / dt.TotalSeconds);
                var sb = new StringBuilder();
                for (var i = 0; i <= j; i++)
                {
                    if (pvMoves[i] == 0)
                    {
                        break;
                    }

                    sb.Append(" ");
                    sb.Append(pvMoves[i].ToUciMoveName());
                }

                Console.WriteLine(
                    $"info depth {depthSearched} score {ScoreToString(bestEval)} nodes {NodesVisited} nps {nps} time {(int)dt.TotalMilliseconds} pv{sb}");
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