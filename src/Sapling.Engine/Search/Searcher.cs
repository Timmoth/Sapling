using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    private const int _pvTableLength = Constants.MaxSearchDepth * (Constants.MaxSearchDepth + 1) / 2;
    private const nuint _pvTableBytes = _pvTableLength * sizeof(uint);
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
    public readonly int* WhiteMaterialCorrHist;
    public readonly int* BlackMaterialCorrHist;

    public Searcher(Transposition* transpositions, int ttCount)
    {
        GC.SuppressFinalize(this);
        Transpositions = transpositions;

        TtMask = (uint)ttCount - 1;
        _pVTable = MemoryHelpers.Allocate<uint>(_pvTableLength);

        WhiteAccumulators = AllocateSearchStack(Constants.MaxSearchDepth + 1);
        BlackAccumulators = AllocateSearchStack(Constants.MaxSearchDepth + 1);
        for (var i = 0; i < Constants.MaxSearchDepth + 1; i++)
        {
            WhiteAccumulators[i] = AllocateAccumulator();
            BlackAccumulators[i] = AllocateAccumulator();
        }

        Boards = MemoryHelpers.Allocate<BoardStateData>(Constants.MaxSearchDepth);
        Accumulators = MemoryHelpers.Allocate<AccumulatorState>(Constants.MaxSearchDepth);

        BucketCacheWhiteBoards = MemoryHelpers.Allocate<BoardStateData>(NnueWeights.InputBuckets * 2);
        BucketCacheBlackBoards = MemoryHelpers.Allocate<BoardStateData>(NnueWeights.InputBuckets * 2);

        BucketCacheWhiteAccumulators = AllocateSearchStack(NnueWeights.InputBuckets * 2);
        BucketCacheBlackAccumulators = AllocateSearchStack(NnueWeights.InputBuckets * 2);
        for (var i = 0; i < NnueWeights.InputBuckets * 2; i++)
        {
            BucketCacheWhiteAccumulators[i] = AllocateAccumulator();
            BucketCacheBlackAccumulators[i] = AllocateAccumulator();
        }

        HashHistory = MemoryHelpers.Allocate<ulong>(800);
        Counters = MemoryHelpers.Allocate<uint>(CountersLength);
        History = MemoryHelpers.Allocate<int>(HistoryLength);
        killers = MemoryHelpers.Allocate<uint>(KillersLength);
        PawnCorrHist = MemoryHelpers.Allocate<int>(TableElementsSize);
        WhiteMaterialCorrHist = MemoryHelpers.Allocate<int>(TableElementsSize);
        BlackMaterialCorrHist = MemoryHelpers.Allocate<int>(TableElementsSize);
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

    ~Searcher()
    {
        if (_pVTable != null)
            NativeMemory.AlignedFree(_pVTable);

        if (HashHistory != null)
            NativeMemory.AlignedFree(HashHistory);

        if (Counters != null)
            NativeMemory.AlignedFree(Counters);

        if (History != null)
            NativeMemory.AlignedFree(History);

        if (killers != null)
            NativeMemory.AlignedFree(killers);

        if (PawnCorrHist != null)
            NativeMemory.AlignedFree(PawnCorrHist);

        if (WhiteMaterialCorrHist != null)
            NativeMemory.AlignedFree(WhiteMaterialCorrHist);

        if (BlackMaterialCorrHist != null)
            NativeMemory.AlignedFree(BlackMaterialCorrHist);

        for (var i = 0; i < Constants.MaxSearchDepth + 1; i++)
        {
            if (WhiteAccumulators[i] != null)
                NativeMemory.AlignedFree(WhiteAccumulators[i]);

            if (BlackAccumulators[i] != null)
                NativeMemory.AlignedFree(BlackAccumulators[i]);
        }

        if (WhiteAccumulators != null)
            NativeMemory.AlignedFree(WhiteAccumulators);

        if (BlackAccumulators != null)
            NativeMemory.AlignedFree(BlackAccumulators);

        for (var i = 0; i < NnueWeights.InputBuckets * 2; i++)
        {
            if (BucketCacheWhiteAccumulators[i] != null)
                NativeMemory.AlignedFree(BucketCacheWhiteAccumulators[i]);

            if (BucketCacheBlackAccumulators[i] != null)
                NativeMemory.AlignedFree(BucketCacheBlackAccumulators[i]);
        }

        if (BucketCacheWhiteAccumulators != null)
            NativeMemory.AlignedFree(BucketCacheWhiteAccumulators);

        if (BucketCacheBlackAccumulators != null)
            NativeMemory.AlignedFree(BucketCacheBlackAccumulators);

        if (BucketCacheWhiteBoards != null)
            NativeMemory.AlignedFree(BucketCacheWhiteBoards);

        if (BucketCacheBlackBoards != null)
            NativeMemory.AlignedFree(BucketCacheBlackBoards);

        if (Boards != null)
            NativeMemory.AlignedFree(Boards);

        if (Accumulators != null)
            NativeMemory.AlignedFree(Accumulators);
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

    public (List<uint> pv, int depthSearched, int score, int nodes) Search(GameState inputBoard, int nodeLimit = 0,
        int depthLimit = 0, bool writeInfo = false)
    {
        NodesVisited = 0;
        BestSoFar = 0;

        var depthSearched = 0;
        _searchCancelled = false;

        NativeMemory.Clear(History, (nuint)HistoryLength * sizeof(int));
        NativeMemory.Clear(Counters, (nuint)CountersLength * sizeof(uint));
        NativeMemory.Clear(killers, (nuint)KillersLength * sizeof(uint));

        NativeMemory.Clear(_pVTable, _pvTableBytes);

        NativeMemory.Clear(Boards, (nuint)sizeof(BoardStateData) * Constants.MaxSearchDepth);
        NativeMemory.Clear(Accumulators, (nuint)sizeof(AccumulatorState) * Constants.MaxSearchDepth);

        NativeMemory.Clear(BucketCacheWhiteBoards, (nuint)sizeof(BoardStateData) * NnueWeights.InputBuckets * 2);
        NativeMemory.Clear(BucketCacheBlackBoards, (nuint)sizeof(BoardStateData) * NnueWeights.InputBuckets * 2);

        Unsafe.CopyBlock(HashHistory, inputBoard.HashHistory, sizeof(ulong) * (uint)inputBoard.Board.TurnCount);

        var alpha = Constants.MinScore;
        var beta = Constants.MaxScore;
        var lastIterationEval = 0;

        var maxDepth = depthLimit > 0 ? depthLimit : Constants.MaxSearchDepth;

        ref var rootBoard = ref Boards[0];

        inputBoard.Board.CloneTo(ref rootBoard);
        FillInitialAccumulators(Boards, Accumulators);

        var bestEval = lastIterationEval = NegaMaxSearch(Boards, Accumulators, 0, 0, alpha, beta, false);

        BestSoFar = _pVTable[0];
        var pvMoves = stackalloc uint[Constants.MaxSearchDepth];

        NativeMemory.Copy(_pVTable, pvMoves, (nuint)Constants.MaxSearchDepth * sizeof(uint));

        var startTime = DateTime.Now;
        for (var j = 1; j < maxDepth; j++)
        {
            var alphaWindowIndex = 0;
            var betaWindowIndex = 0;
            do
            {
                alpha = lastIterationEval - GetAsperationWindow(alphaWindowIndex);
                beta = lastIterationEval + GetAsperationWindow(betaWindowIndex);

                var eval = NegaMaxSearch(Boards, Accumulators, 0, j, alpha, beta, false);

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