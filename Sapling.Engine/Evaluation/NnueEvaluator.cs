using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    private const int Scale = 400;
    private const int Q = 255 * 64;

#if AVX512
            const int VectorSize = 32; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#else
    public const int VectorSize = 16; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#endif

    public const int AccumulatorSize = NnueWeights.Layer1Size / VectorSize;

    public const int L1ByteSize = sizeof(short) * NnueWeights.Layer1Size;
    public const int InputBucketWeightCount = NnueWeights.InputSize * AccumulatorSize;

    public const int BucketDivisor = (32 + NnueWeights.OutputBuckets - 1) / NnueWeights.OutputBuckets;

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

#if AVX512
        var output = board.Data.WhiteToMove
            ? ForwardCReLU512(board.WhiteAccumulator, board.BlackAccumulator, bucket)
            : ForwardCReLU512(board.BlackAccumulator, board.WhiteAccumulator, bucket);
#else
        var output = boardState->WhiteToMove
            ? ForwardCReLU256(WhiteAccumulators[depthFromRoot], BlackAccumulators[depthFromRoot], bucket)
            : ForwardCReLU256(BlackAccumulators[depthFromRoot], WhiteAccumulators[depthFromRoot], bucket);

#endif

        return (eval = (output + NnueWeights.OutputBiases[bucket]) * Scale / Q);
    }

    public void FillInitialAccumulators(BoardStateData* board, AccumulatorState* accumulatorState)
    {
        VectorShort* whiteAcc = WhiteAccumulators[0];
        VectorShort* blackAcc = BlackAccumulators[0];

        accumulatorState->UpdateTo(board);
        accumulatorState->WhiteAccumulatorUpToDate = accumulatorState->BlackAccumulatorUpToDate = true;

#if AVX512
        SimdResetAccumulators512(whiteAcc, blackAcc);
#else
        SimdResetAccumulators256(whiteAcc, blackAcc);
#endif
        var whiteMirrored = accumulatorState->WhiteMirrored ? 1 : 0;
        var blackMirrored = accumulatorState->BlackMirrored ? 1 : 0;

        var wFeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteInputBucket * InputBucketWeightCount;
        var bFeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackInputBucket * InputBucketWeightCount;
        var i = Constants.WhiteKing * 128 + board->WhiteKingSquare * 2;
        var wIdx = NnueExtensions.WhiteFeatureIndexes[i+ whiteMirrored];
        var bIdx = NnueExtensions.BlackFeatureIndexes[i+ blackMirrored];

#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
        AddWeights256(whiteAcc, wFeaturePtr + wIdx);
        AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif

        var bitboard = board->Occupancy[Constants.WhitePawn];
        while (bitboard != 0)
        {
            i = Constants.WhitePawn * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

        bitboard = board->Occupancy[Constants.WhiteKnight];
        while (bitboard != 0)
        {
            i = Constants.WhiteKnight * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

        bitboard = board->Occupancy[Constants.WhiteBishop];
        while (bitboard != 0)
        {
            i = Constants.WhiteBishop * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

        bitboard = board->Occupancy[Constants.WhiteRook];
        while (bitboard != 0)
        {
            i = Constants.WhiteRook * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

        bitboard = board->Occupancy[Constants.WhiteQueen];
        while (bitboard != 0)
        {
            i = Constants.WhiteQueen * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

        i = Constants.BlackKing * 128 + board->BlackKingSquare * 2;
        wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
        bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
        AddWeights256(whiteAcc, wFeaturePtr + wIdx);
        AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif

        bitboard = board->Occupancy[Constants.BlackPawn];
        while (bitboard != 0)
        {
            i = Constants.BlackPawn * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

        bitboard = board->Occupancy[Constants.BlackKnight];
        while (bitboard != 0)
        {
            i = Constants.BlackKnight * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

        bitboard = board->Occupancy[Constants.BlackBishop];
        while (bitboard != 0)
        {
            i = Constants.BlackBishop * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

        bitboard = board->Occupancy[Constants.BlackRook];
        while (bitboard != 0)
        {
            i = Constants.BlackRook * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

        bitboard = board->Occupancy[Constants.BlackQueen];
        while (bitboard != 0)
        {
            i = Constants.BlackQueen * 128 + bitboard.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + whiteMirrored];
            bIdx = NnueExtensions.BlackFeatureIndexes[i + blackMirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }
    }

    
    private static void FullRefreshWhite(BoardStateData* board, int mirrored, int bucket, VectorShort* whiteAcc)
    {
#if AVX512
        SimdCopy512(whiteAcc, NnueWeights.FeatureBiases);
#else
        Unsafe.CopyBlock(whiteAcc, NnueWeights.FeatureBiases, L1ByteSize);
#endif


        var wFeaturePtr = NnueWeights.FeatureWeights + bucket * InputBucketWeightCount;
        var i = Constants.WhiteKing * 128 + board->WhiteKingSquare * 2;
        var wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
        AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif

        var bitboards = board->Occupancy[Constants.WhitePawn];

        while (bitboards != 0)
        {
            i = Constants.WhitePawn * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];

#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }

        bitboards = board->Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            i = Constants.WhiteKnight * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }

        bitboards = board->Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            i = Constants.WhiteBishop * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }

        bitboards = board->Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            i = Constants.WhiteRook * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }

        bitboards = board->Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            i = Constants.WhiteQueen * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }
        
        i = Constants.BlackKing * 128 + board->BlackKingSquare * 2;
        wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
        AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif

        bitboards = board->Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            i = Constants.BlackPawn * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }

        bitboards = board->Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            i = Constants.BlackKnight * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }

        bitboards = board->Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            i = Constants.BlackBishop * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }

        bitboards = board->Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            i = Constants.BlackRook * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }

        bitboards = board->Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            i = Constants.BlackQueen * 128 + bitboards.PopLSB() * 2;
            wIdx = NnueExtensions.WhiteFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx);
#endif
        }
    }

    private static void FullRefreshBlack(BoardStateData* board, int mirrored, int bucket, VectorShort* blackAcc)
    {

#if AVX512
        SimdCopy512(blackAcc, NnueWeights.FeatureBiases);
#else
        Unsafe.CopyBlock(blackAcc, NnueWeights.FeatureBiases, L1ByteSize);

#endif

        var bFeaturePtr = NnueWeights.FeatureWeights + bucket * InputBucketWeightCount;
        var i = Constants.WhiteKing * 128 + board->WhiteKingSquare * 2;
        var bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
        AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        var bitboards = board->Occupancy[Constants.WhitePawn];
        while (bitboards != 0)
        {
            i = Constants.WhitePawn * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

            bitboards = board->Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            i = Constants.WhiteKnight * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

            bitboards = board->Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            i = Constants.WhiteBishop * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

            bitboards = board->Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            i = Constants.WhiteRook * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

            bitboards = board->Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            i = Constants.WhiteQueen * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }
        i = Constants.BlackKing * 128 + board->BlackKingSquare * 2;
        bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif

        bitboards = board->Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            i = Constants.BlackPawn * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

            bitboards = board->Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            i = Constants.BlackKnight * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

            bitboards = board->Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            i = Constants.BlackBishop * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

            bitboards = board->Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            i = Constants.BlackRook * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
        }

            bitboards = board->Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            i = Constants.BlackQueen * 128 + bitboards.PopLSB() * 2;
            bIdx = NnueExtensions.BlackFeatureIndexes[i + mirrored];
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx);
#endif
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

#if AVX512
            SubAdd512(prevAccumulator, accumulator,
                sub1FeaturePtr,
                add1FeaturePtr);
#else
            SubAdd256(prevAccumulator, accumulator,
                sub1FeaturePtr,
                add1FeaturePtr);
#endif

        }
        else if (changeType == AccumulatorChangeType.SubSubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteSubFeatureUpdatesA;
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteSubFeatureUpdatesB;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteAddFeatureUpdatesA;

#if AVX512
            SubSubAdd512(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);
#else
            SubSubAdd256(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);
#endif

        }
        else if (changeType == AccumulatorChangeType.SubSubAddAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteSubFeatureUpdatesA;
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteSubFeatureUpdatesB;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteAddFeatureUpdatesA;
            var add2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->WhiteAddFeatureUpdatesB;

#if AVX512
           SubSubAddAdd512(prevAccumulator, accumulator, sub1FeaturePtr,
                sub2FeaturePtr,
                add1FeaturePtr,
                add2FeaturePtr);
#else
            SubSubAddAdd256(prevAccumulator, accumulator, sub1FeaturePtr,
                sub2FeaturePtr,
                add1FeaturePtr,
                add2FeaturePtr);
#endif
        }
        else if (changeType == AccumulatorChangeType.None)
        {

#if AVX512
            SimdCopy512(accumulator, prevAccumulator);
#else
            Unsafe.CopyBlock(accumulator, prevAccumulator, L1ByteSize);

#endif

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

#if AVX512
            SubAdd512(prevAccumulator, accumulator,
                sub1FeaturePtr,
                add1FeaturePtr);
#else
            SubAdd256(prevAccumulator, accumulator,
                sub1FeaturePtr,
                add1FeaturePtr);
#endif

        }
        else if (changeType == AccumulatorChangeType.SubSubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackSubFeatureUpdatesA;
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackSubFeatureUpdatesB;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackAddFeatureUpdatesA;

#if AVX512
            SubSubAdd512(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);

#else
            SubSubAdd256(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);
#endif

        }
        else if (changeType == AccumulatorChangeType.SubSubAddAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackSubFeatureUpdatesA;
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackSubFeatureUpdatesB;
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackAddFeatureUpdatesA;
            var add2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState->BlackAddFeatureUpdatesB;

#if AVX512
            SubSubAddAdd512(prevAccumulator, accumulator, sub1FeaturePtr,
                sub2FeaturePtr,
                add1FeaturePtr,
                add2FeaturePtr);

#else
            SubSubAddAdd256(prevAccumulator, accumulator, sub1FeaturePtr,
                sub2FeaturePtr,
                add1FeaturePtr,
                add2FeaturePtr);
#endif


        }
        else if (changeType == AccumulatorChangeType.None)
        {
#if AVX512
            SimdCopy512(accumulator, prevAccumulator);
#else
            Unsafe.CopyBlock(accumulator, prevAccumulator, L1ByteSize);
#endif
        }

        accumulatorState->BlackAccumulatorUpToDate = true;
    }

    public void RefreshAccumulatorWhite(BoardStateData* boards, AccumulatorState* accumulators, int depthFromRoot)
    {
        VectorShort* accumulator = WhiteAccumulators[depthFromRoot];

        accumulators->Evaluation = TranspositionTableExtensions.NoHashEntry;
        accumulators->WhiteNeedsRefresh = false;
        accumulators->WhiteMirrored = boards->WhiteKingSquare.IsMirroredSide();
        accumulators->WhiteInputBucket = *(NnueWeights.BucketLayout + boards->WhiteKingSquare);
        accumulators->WhiteAccumulatorUpToDate = true;

        var mirrored = accumulators->WhiteMirrored ? 1 : 0;
        var bucketCacheIndex = accumulators->WhiteInputBucket * 2 + mirrored;
        var cachedBoard = BucketCacheWhiteBoards + bucketCacheIndex;
        var cachedAccumulator = BucketCacheWhiteAccumulators[bucketCacheIndex];

        if (cachedBoard->PieceCount == 0)
        {
            FullRefreshWhite(boards, mirrored, accumulators->WhiteInputBucket, accumulator);
#if AVX512
            SimdCopy512(accumulator, cachedAccumulator);
#else
            Unsafe.CopyBlock(cachedAccumulator, accumulator, L1ByteSize);
#endif
            Unsafe.CopyBlock(cachedBoard, boards, BoardStateExtensions.BoardStateSize);
            return;
        }

        var bucketOffset = NnueWeights.FeatureWeights + accumulators->WhiteInputBucket * InputBucketWeightCount;
        if (boards->BlackKingSquare != cachedBoard->BlackKingSquare)
        {
            var subKing = bucketOffset + NnueExtensions.WhiteFeatureIndexes[Constants.BlackKing * 128 + cachedBoard->BlackKingSquare * 2 + mirrored];
            var addKing = bucketOffset + NnueExtensions.WhiteFeatureIndexes[Constants.BlackKing * 128 + boards->BlackKingSquare * 2 + mirrored];

#if AVX512
            SubAdd512(cachedAccumulator, cachedAccumulator, subKing, addKing);
#else
            SubAdd256(cachedAccumulator, cachedAccumulator, subKing, addKing);
#endif

        }

        if (boards->WhiteKingSquare != cachedBoard->WhiteKingSquare)
        {
            var subKing = bucketOffset + NnueExtensions.WhiteFeatureIndexes[Constants.WhiteKing * 128 + cachedBoard->WhiteKingSquare * 2 + mirrored];
            var addKing = bucketOffset + NnueExtensions.WhiteFeatureIndexes[Constants.WhiteKing * 128 + boards->WhiteKingSquare * 2 + mirrored];

#if AVX512
            SubAdd512(cachedAccumulator, cachedAccumulator, subKing, addKing);
#else
            SubAdd256(cachedAccumulator, cachedAccumulator, subKing, addKing);
#endif
        }

            for (int i = Constants.BlackPawn; i < Constants.BlackKing; i++)
        {
            var prev = cachedBoard->Occupancy[i];
            var curr = boards->Occupancy[i];

            var added = curr & ~prev;
            var removed = prev & ~curr;

            while (added != 0)
            {
                var idx = NnueExtensions.WhiteFeatureIndexes[i * 128 + added.PopLSB() * 2 + mirrored];

#if AVX512
                AddWeights512(cachedAccumulator, bucketOffset + idx);
#else
                AddWeights256(cachedAccumulator, bucketOffset + idx);
#endif
            }

            while (removed != 0)
            {
                var idx = NnueExtensions.WhiteFeatureIndexes[i * 128 + removed.PopLSB() * 2 + mirrored];
#if AVX512
                Sub512(cachedAccumulator, cachedAccumulator, bucketOffset + idx);
#else
                Sub256(cachedAccumulator, cachedAccumulator, bucketOffset + idx);
#endif
            }

        }

#if AVX512
        SimdCopy512(accumulator, cachedAccumulator);
#else
        Unsafe.CopyBlock(accumulator, cachedAccumulator, L1ByteSize);
#endif

        Unsafe.CopyBlock(cachedBoard, boards, BoardStateExtensions.BoardStateSize);
    }


    public void RefreshAccumulatorBlack(BoardStateData* board, AccumulatorState* accumulatorState, int depthFromRoot)
    {
        VectorShort* accumulator = BlackAccumulators[depthFromRoot];

        accumulatorState->Evaluation = TranspositionTableExtensions.NoHashEntry;
        accumulatorState->BlackNeedsRefresh = false;
        accumulatorState->BlackMirrored = board->BlackKingSquare.IsMirroredSide();
        accumulatorState->BlackInputBucket = *(NnueWeights.BucketLayout + (board->BlackKingSquare ^ 0x38));
        accumulatorState->BlackAccumulatorUpToDate = true;

        var mirrored = accumulatorState->BlackMirrored ? 1 : 0;
        var bucketCacheIndex = accumulatorState->BlackInputBucket * 2 + mirrored;

        var cachedBoard = BucketCacheBlackBoards + bucketCacheIndex;
        var cachedAccumulator = BucketCacheBlackAccumulators[bucketCacheIndex];

        if (cachedBoard->PieceCount == 0)
        {
            FullRefreshBlack(board, mirrored, accumulatorState->BlackInputBucket, accumulator);
#if AVX512
            SimdCopy512(accumulator, cachedAccumulator);
#else
            Unsafe.CopyBlock(cachedAccumulator, accumulator, L1ByteSize);
#endif
            Unsafe.CopyBlock(cachedBoard, board, BoardStateExtensions.BoardStateSize);
            return;
        }

        var bucketOffset = NnueWeights.FeatureWeights + accumulatorState->BlackInputBucket * InputBucketWeightCount;
        if (board->BlackKingSquare != cachedBoard->BlackKingSquare)
        {
            var subKing = bucketOffset + NnueExtensions.BlackFeatureIndexes[Constants.BlackKing * 128 + cachedBoard->BlackKingSquare * 2 + mirrored];
            var addKing = bucketOffset + NnueExtensions.BlackFeatureIndexes[Constants.BlackKing * 128 + board->BlackKingSquare * 2 + mirrored];

#if AVX512
            SubAdd512(cachedAccumulator, cachedAccumulator, subKing, addKing);
#else
            SubAdd256(cachedAccumulator, cachedAccumulator, subKing, addKing);
#endif

        }

        if (board->WhiteKingSquare != cachedBoard->WhiteKingSquare)
        {
            var subKing = bucketOffset + NnueExtensions.BlackFeatureIndexes[Constants.WhiteKing * 128 + cachedBoard->WhiteKingSquare * 2 + mirrored];
            var addKing = bucketOffset + NnueExtensions.BlackFeatureIndexes[Constants.WhiteKing * 128 + board->WhiteKingSquare * 2 + mirrored];

#if AVX512
            SubAdd512(cachedAccumulator, cachedAccumulator, subKing, addKing);
#else
            SubAdd256(cachedAccumulator, cachedAccumulator, subKing, addKing);
#endif
        }

        for (int i = Constants.BlackPawn; i < Constants.BlackKing; i++)
        {
            var prev = cachedBoard->Occupancy[i];
            var curr = board->Occupancy[i];

            var added = curr & ~prev;
            var removed = prev & ~curr;

            while (added != 0)
            {
                var idx = NnueExtensions.BlackFeatureIndexes[i * 128 + added.PopLSB() * 2 + mirrored];
#if AVX512
                AddWeights512(cachedAccumulator, bucketOffset + idx);
#else
                AddWeights256(cachedAccumulator, bucketOffset + idx);
#endif
            }

            while (removed != 0)
            {
                var idx = NnueExtensions.BlackFeatureIndexes[i * 128 + removed.PopLSB() * 2 + mirrored];
#if AVX512
                Sub512(cachedAccumulator, cachedAccumulator, bucketOffset + idx);
#else
                Sub256(cachedAccumulator, cachedAccumulator, bucketOffset + idx);
#endif
            }
        }

#if AVX512
        SimdCopy512(accumulator, cachedAccumulator);
#else
        Unsafe.CopyBlock(accumulator, cachedAccumulator, L1ByteSize);
#endif

        Unsafe.CopyBlock(cachedBoard, board, BoardStateExtensions.BoardStateSize);
    }
}