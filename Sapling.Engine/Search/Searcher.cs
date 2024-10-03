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
    private const int KillersLength = Constants.MaxSearchDepth * 2;
  
    private const int HistoryLength = 13 * 64;
    private const int CountersLength = 13 * 64;
    public readonly BoardStateData* BucketCacheWhiteBoards;
    public readonly VectorShort** BucketCacheWhiteAccumulators;
    public readonly BoardStateData* BucketCacheBlackBoards;
    public readonly VectorShort** BucketCacheBlackAccumulators;
    public readonly VectorShort** WhiteAccumulators;
    public readonly VectorShort** BlackAccumulators;
    public readonly ulong* HashHistory;

    public readonly BoardStateData* Boards;
    public readonly AccumulatorState* Accumulators;


    public readonly int* History;
    public readonly uint* Counters;
    public readonly uint* killers;
    public readonly int* PawnCorrHist;

    public Searcher(Transposition* transpositions, int ttCount)
    {
        Transpositions = transpositions;

        TtMask = (uint)ttCount - 1;
        _pVTable = AlignedAllocZeroed();

        WhiteAccumulators = AllocateSearchStack(Constants.MaxSearchDepth + 1);
        BlackAccumulators = AllocateSearchStack(Constants.MaxSearchDepth + 1);
        for (var i = 0; i < Constants.MaxSearchDepth + 1; i++)
        {
            WhiteAccumulators[i] = AllocateAccumulator();
            BlackAccumulators[i] = AllocateAccumulator();
        }

        Boards = AllocateBoardState(Constants.MaxSearchDepth);
        Accumulators = AllocateAccumulatorState(Constants.MaxSearchDepth);

        BucketCacheWhiteBoards = AllocateBoardState(NnueWeights.InputBuckets * 2);
        BucketCacheBlackBoards = AllocateBoardState(NnueWeights.InputBuckets * 2);

        BucketCacheWhiteAccumulators = AllocateSearchStack(NnueWeights.InputBuckets * 2);
        BucketCacheBlackAccumulators = AllocateSearchStack(NnueWeights.InputBuckets * 2);
        for (var i = 0; i < NnueWeights.InputBuckets * 2; i++)
        {
            BucketCacheWhiteAccumulators[i] = AllocateAccumulator();
            BucketCacheBlackAccumulators[i] = AllocateAccumulator();
        }

        HashHistory = AllocateUlong(800);
        Counters = AllocateUInt((nuint)CountersLength);
        History = AllocateInt((nuint)HistoryLength);
        killers = AllocateUInt((nuint)KillersLength);
        PawnCorrHist = AllocateInt((nuint)TableElementsSize);


    }

    public static VectorShort* AllocateAccumulator()
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)Searcher.L1ByteSize, alignment);
        NativeMemory.Clear(block, (nuint)Searcher.L1ByteSize);

        return (VectorShort*)block;
    }

    public static unsafe VectorShort** AllocateSearchStack(nuint items)
    {
        const nuint alignment = 64;

        nuint bytes = ((nuint)sizeof(VectorShort*) * (nuint)items);
        void* block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (VectorShort**)block;
    }
    public static unsafe AccumulatorState* AllocateAccumulatorState(nuint items)
    {
        const nuint alignment = 64;

        nuint bytes = ((nuint)sizeof(AccumulatorState) * (nuint)items);
        void* block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (AccumulatorState*)block;
    }
    public static unsafe BoardStateData* AllocateBoardState(nuint items)
    {
        const nuint alignment = 64;

        nuint bytes = ((nuint)sizeof(BoardStateData) * (nuint)items);
        void* block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (BoardStateData*)block;
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
        NativeMemory.AlignedFree(HashHistory);
        NativeMemory.AlignedFree(Counters);
        NativeMemory.AlignedFree(History);
        NativeMemory.AlignedFree(killers);

        for (var i = 0; i < Constants.MaxSearchDepth + 1; i++)
        {
            NativeMemory.AlignedFree(WhiteAccumulators[i]);
            NativeMemory.AlignedFree(BlackAccumulators[i]);
        }

        NativeMemory.AlignedFree(WhiteAccumulators);
        NativeMemory.AlignedFree(BlackAccumulators);

        for (var i = 0; i < Constants.MaxSearchDepth + 1; i++)
        {
            NativeMemory.AlignedFree(BucketCacheWhiteAccumulators[i]);
            NativeMemory.AlignedFree(BucketCacheBlackAccumulators[i]);
        }

        NativeMemory.AlignedFree(BucketCacheWhiteAccumulators);
        NativeMemory.AlignedFree(BucketCacheBlackAccumulators);

        NativeMemory.AlignedFree(BucketCacheWhiteBoards);
        NativeMemory.AlignedFree(BucketCacheBlackBoards);  
        
        NativeMemory.AlignedFree(Boards);
        NativeMemory.AlignedFree(Accumulators);
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


    public (List<uint> pv, int depthSearched, int score, int nodes) Search(GameState inputBoard, int nodeLimit = 0,
        int depthLimit = 0, bool writeInfo = false)
    {
        NodesVisited = 0;
        BestSoFar = 0;

        var depthSearched = 0;
        _searchCancelled = false;
        //NativeMemory.Clear(PawnCorrHist, (nuint)TableElementsSize);

        NativeMemory.Clear(History, (nuint)HistoryLength);
        NativeMemory.Clear(Counters, (nuint)CountersLength);
        NativeMemory.Clear(killers, (nuint)KillersLength);

        NativeMemory.Clear(_pVTable, _pvTableBytes);

        NativeMemory.Clear(Boards, (nuint)sizeof(BoardStateData) * Constants.MaxSearchDepth);
        NativeMemory.Clear(Accumulators, (nuint)sizeof(AccumulatorState) * Constants.MaxSearchDepth);

        Unsafe.CopyBlock(HashHistory, inputBoard.HashHistory, sizeof(ulong) * (uint)inputBoard.Board.TurnCount);

        var alpha = Constants.MinScore;
        var beta = Constants.MaxScore;
        var lastIterationEval = 0;

        var maxDepth = depthLimit > 0 ? depthLimit : Constants.MaxSearchDepth;

        ref var rootBoard = ref Boards[0];

        inputBoard.Board.CloneTo(ref rootBoard);
        FillInitialAccumulators(Boards, Accumulators);

        var bestEval = lastIterationEval = NegaMaxSearch(Boards, Accumulators, 0, 0, alpha, beta);

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

                NativeMemory.Clear(killers, (nuint)KillersLength);
                NativeMemory.Clear(Counters, (nuint)CountersLength);

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