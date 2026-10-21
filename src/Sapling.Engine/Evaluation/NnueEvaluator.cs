using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    private const int Scale = 400;
    private const int QA = 255;
    private const int QB = 64;
    private const int Q = QA * QB;

#if AVX512
            const int VectorSize = 32; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#else
    public const int VectorSize = 16; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#endif

    public const int AccumulatorSize = NnueWeights.Layer1Size;

    public const int L1ByteSize = sizeof(short) * NnueWeights.Layer1Size;
    public const int InputBucketWeightCount = NnueWeights.InputSize * AccumulatorSize;

    public const int BucketDivisor = (32 + NnueWeights.OutputBuckets - 1) / NnueWeights.OutputBuckets;

    // Narrow test entry point to the generated output kernel for the friend test assembly
    // (output-parity tests in NnueOracleTests). Never called from production search paths.
    internal static int ForwardSCReLUProbe(short* usData, short* themData, int bucket) =>
        ForwardSCReLU(usData, themData, bucket);

    public int Evaluate(BoardStateData* boardState, AccumulatorState* accumulatorState, int depthFromRoot)
    {
        ref var eval = ref accumulatorState->Evaluation;
        if (eval < TranspositionTableExtensions.NoHashEntry)
        {
            return eval;
        }

        UpdateWhiteAccumulator(boardState, accumulatorState, depthFromRoot);
        UpdateBlackAccumulator(boardState, accumulatorState, depthFromRoot);

        var bucket = (boardState->PieceCount - 2) / BucketDivisor;

        var output = boardState->WhiteToMove
            ? ForwardSCReLU(WhiteAccumulators[depthFromRoot], BlackAccumulators[depthFromRoot], bucket)
            : ForwardSCReLU(BlackAccumulators[depthFromRoot], WhiteAccumulators[depthFromRoot], bucket);

        // SCReLU squares the activation, so the raw sum carries an extra factor of QA that
        // has to come out before the output bias (quantised at QA * QB) is added.
        return (eval = (output / QA + NnueWeights.OutputBiases[bucket]) * Scale / Q);
    }

    public void FillInitialAccumulators(BoardStateData* board, AccumulatorState* accumulatorState)
    {
        short* whiteAcc = WhiteAccumulators[0];
        short* blackAcc = BlackAccumulators[0];

        accumulatorState->UpdateTo(board);
        accumulatorState->WhiteAccumulatorUpToDate = accumulatorState->BlackAccumulatorUpToDate = true;

        SimdResetAccumulators(whiteAcc, blackAcc);
        
        var whiteMirrored = accumulatorState->WhiteMirrored ? 1 : 0;
        var blackMirrored = accumulatorState->BlackMirrored ? 1 : 0;

        var wFeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteInputBucket * InputBucketWeightCount;
        var bFeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackInputBucket * InputBucketWeightCount;

        var whiteFeatureIndexes = NnueExtensions.WhiteFeatureIndexes + whiteMirrored;
        var blackFeatureIndexes = NnueExtensions.BlackFeatureIndexes + blackMirrored;

        var i = Constants.WhiteKingFeatureIndexOffset + (board->WhiteKingSquare << 1);
        AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
        AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));

        var bitboard = board->Occupancy[Constants.WhitePawn];
        while (bitboard != 0)
        {
            i = Constants.WhitePawnFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }

        bitboard = board->Occupancy[Constants.WhiteKnight];
        while (bitboard != 0)
        {
            i = Constants.WhiteKnightFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }

        bitboard = board->Occupancy[Constants.WhiteBishop];
        while (bitboard != 0)
        {
            i = Constants.WhiteBishopFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }

        bitboard = board->Occupancy[Constants.WhiteRook];
        while (bitboard != 0)
        {
            i = Constants.WhiteRookFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }

        bitboard = board->Occupancy[Constants.WhiteQueen];
        while (bitboard != 0)
        {
            i = Constants.WhiteQueenFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }

        i = Constants.BlackKingFeatureIndexOffset + (board->BlackKingSquare<< 1);
        AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
        AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));

        bitboard = board->Occupancy[Constants.BlackPawn];
        while (bitboard != 0)
        {
            i = Constants.BlackPawnFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }

        bitboard = board->Occupancy[Constants.BlackKnight];
        while (bitboard != 0)
        {
            i = Constants.BlackKnightFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }

        bitboard = board->Occupancy[Constants.BlackBishop];
        while (bitboard != 0)
        {
            i = Constants.BlackBishopFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }

        bitboard = board->Occupancy[Constants.BlackRook];
        while (bitboard != 0)
        {
            i = Constants.BlackRookFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }

        bitboard = board->Occupancy[Constants.BlackQueen];
        while (bitboard != 0)
        {
            i = Constants.BlackQueenFeatureIndexOffset + (bitboard.PopLSB()<< 1);
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + i));
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + i));
        }
    }

    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateWhiteAccumulator(BoardStateData* board, AccumulatorState* accumulator, int depthFromRoot)
    {
        // Directly dereference the pointers for the initial checks since they are at the correct index.
        if (accumulator->WhiteAccumulatorUpToDate)
        {
            return;
        }

        if (accumulator->WhiteNeedsRefresh)
        {
            // No need to adjust the pointers since they are at the correct index
            RefreshAccumulatorWhite(board, accumulator, depthFromRoot);
            return;
        }

        // Start searching for the nearest valid state moving backwards.
        int refreshDepth = depthFromRoot - 1;
        while (refreshDepth >= 0)
        {
            AccumulatorState* parentAccumulator = (accumulator - (depthFromRoot - refreshDepth));

            // If an up-to-date accumulator is found, stop at the next level.
            if (parentAccumulator->WhiteAccumulatorUpToDate)
            {
                refreshDepth++;
                break;
            }

            // If a refresh is needed at this depth, perform the refresh and stop.
            if (parentAccumulator->WhiteNeedsRefresh)
            {
                RefreshAccumulatorWhite(board - (depthFromRoot - refreshDepth), parentAccumulator, refreshDepth);
                refreshDepth++;
                break;
            }

            refreshDepth--;
        }

        // Now, move forward and apply updates up to the current depth.
        for (int i = refreshDepth; i <= depthFromRoot; i++)
        {
            ApplyWhiteUpdate(
               i,
                accumulator - (depthFromRoot - i)
            );
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateBlackAccumulator(BoardStateData* board, AccumulatorState* accumulator, int depthFromRoot)
    {
        // Directly dereference the pointers for the initial checks since they are at the correct index.
        if (accumulator->BlackAccumulatorUpToDate)
        {
            return;
        }

        if (accumulator->BlackNeedsRefresh)
        {
            // No need to adjust the pointers since they are at the correct index
            RefreshAccumulatorBlack(board, accumulator, depthFromRoot);
            return;
        }

        // Start searching for the nearest valid state moving backwards.
        int refreshDepth = depthFromRoot - 1;
        while (refreshDepth >= 0)
        {
            AccumulatorState* parentAccumulator = (accumulator - (depthFromRoot - refreshDepth));

            // If an up-to-date accumulator is found, stop at the next level.
            if (parentAccumulator->BlackAccumulatorUpToDate)
            {
                refreshDepth++;
                break;
            }

            // If a refresh is needed at this depth, perform the refresh and stop.
            if (parentAccumulator->BlackNeedsRefresh)
            {
                RefreshAccumulatorBlack(board - (depthFromRoot - refreshDepth), parentAccumulator, refreshDepth);
                refreshDepth++;
                break;
            }

            refreshDepth--;
        }

        // Now, move forward and apply updates up to the current depth.
        for (int i = refreshDepth; i <= depthFromRoot; i++)
        {
            ApplyBlackUpdate(i,
                accumulator - (depthFromRoot - i)
            );
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ApplyWhiteUpdate(int depthFromRoot, AccumulatorState* accumulatorState)
    {
        var prevAccumulator = WhiteAccumulators[depthFromRoot - 1];
        var accumulator = WhiteAccumulators[depthFromRoot];

        var changeType = accumulatorState->ChangeType;
        if (changeType == AccumulatorChangeType.SubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteSubFeatureUpdatesA;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteAddFeatureUpdatesA;

            SubAdd(prevAccumulator, accumulator,
                sub1FeaturePtr,
                add1FeaturePtr);

        }
        else if (changeType == AccumulatorChangeType.SubSubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteSubFeatureUpdatesA;
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteSubFeatureUpdatesB;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteAddFeatureUpdatesA;
            SubSubAdd(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);

        }
        else if (changeType == AccumulatorChangeType.SubSubAddAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteSubFeatureUpdatesA;
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteSubFeatureUpdatesB;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteAddFeatureUpdatesA;
            var add2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteAddFeatureUpdatesB;

            SubSubAddAdd(prevAccumulator, accumulator, sub1FeaturePtr,
                sub2FeaturePtr,
                add1FeaturePtr,
                add2FeaturePtr);
        }
        else if (changeType == AccumulatorChangeType.None)
        {
            Unsafe.CopyBlock(accumulator, prevAccumulator, L1ByteSize);
        }

        accumulatorState->WhiteAccumulatorUpToDate = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ApplyBlackUpdate(int depthFromRoot, AccumulatorState* accumulatorState)
    {
        var prevAccumulator = BlackAccumulators[depthFromRoot - 1];
        var accumulator = BlackAccumulators[depthFromRoot];

        var changeType = accumulatorState->ChangeType;
        if (changeType == AccumulatorChangeType.SubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackSubFeatureUpdatesA;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackAddFeatureUpdatesA;

            SubAdd(prevAccumulator, accumulator,
                sub1FeaturePtr,
                add1FeaturePtr);
        }
        else if (changeType == AccumulatorChangeType.SubSubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackSubFeatureUpdatesA;
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackSubFeatureUpdatesB;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackAddFeatureUpdatesA;

            SubSubAdd(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);


        }
        else if (changeType == AccumulatorChangeType.SubSubAddAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackSubFeatureUpdatesA;
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackSubFeatureUpdatesB;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackAddFeatureUpdatesA;
            var add2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackAddFeatureUpdatesB;
            SubSubAddAdd(prevAccumulator, accumulator, sub1FeaturePtr,
                sub2FeaturePtr,
                add1FeaturePtr,
                add2FeaturePtr);

        }
        else if (changeType == AccumulatorChangeType.None)
        {
            Unsafe.CopyBlock(accumulator, prevAccumulator, L1ByteSize);
        }

        accumulatorState->BlackAccumulatorUpToDate = true;
    }

    public void RefreshAccumulatorWhite(BoardStateData* board, AccumulatorState* state, int ply)
    {
        state->Evaluation = TranspositionTableExtensions.NoHashEntry;
        state->WhiteNeedsRefresh = false;
        state->WhiteMirrored = board->WhiteKingSquare.IsMirroredSide();
        state->WhiteInputBucket = *(NnueWeights.BucketLayout + board->WhiteKingSquare);
        state->WhiteAccumulatorUpToDate = true;
        var mirrored = state->WhiteMirrored ? 1 : 0;
        var index = (state->WhiteInputBucket << 1) + mirrored;
        RefreshAccumulatorTiled(board, BucketCacheWhiteBoards + index,
            BucketCacheWhiteAccumulators[index], WhiteAccumulators[ply],
            NnueExtensions.WhiteFeatureIndexes + mirrored, state->WhiteInputBucket);
    }

    public void RefreshAccumulatorBlack(BoardStateData* board, AccumulatorState* state, int ply)
    {
        state->Evaluation = TranspositionTableExtensions.NoHashEntry;
        state->BlackNeedsRefresh = false;
        state->BlackMirrored = board->BlackKingSquare.IsMirroredSide();
        state->BlackInputBucket = *(NnueWeights.BucketLayout + (board->BlackKingSquare ^ 0x38));
        state->BlackAccumulatorUpToDate = true;
        var mirrored = state->BlackMirrored ? 1 : 0;
        var index = (state->BlackInputBucket << 1) + mirrored;
        RefreshAccumulatorTiled(board, BucketCacheBlackBoards + index,
            BucketCacheBlackAccumulators[index], BlackAccumulators[ply],
            NnueExtensions.BlackFeatureIndexes + mirrored, state->BlackInputBucket);
    }
}
