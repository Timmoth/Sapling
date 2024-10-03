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

        var output = boardState->WhiteToMove
            ? ForwardCReLU(WhiteAccumulators[depthFromRoot], BlackAccumulators[depthFromRoot], bucket)
            : ForwardCReLU(BlackAccumulators[depthFromRoot], WhiteAccumulators[depthFromRoot], bucket);

        return (eval = (output + NnueWeights.OutputBiases[bucket]) * Scale / Q);
    }

    public void FillInitialAccumulators(BoardStateData* board, AccumulatorState* accumulatorState)
    {
        VectorShort* whiteAcc = WhiteAccumulators[0];
        VectorShort* blackAcc = BlackAccumulators[0];

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

    
    private static void FullRefreshWhite(BoardStateData* board, int mirrored, int bucket, VectorShort* whiteAcc)
    {
        Unsafe.CopyBlock(whiteAcc, NnueWeights.FeatureBiases, L1ByteSize);


        var wFeaturePtr = NnueWeights.FeatureWeights + bucket * InputBucketWeightCount;
        var whiteFeatureIndexes = NnueExtensions.WhiteFeatureIndexes + mirrored;

        AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.WhiteKingFeatureIndexOffset + (board->WhiteKingSquare << 1)));


        var bitboards = board->Occupancy[Constants.WhitePawn];

        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.WhitePawnFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.WhiteKnightFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.WhiteBishopFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.WhiteRookFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.WhiteQueenFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.BlackKingFeatureIndexOffset + (board->BlackKingSquare << 1)));

        bitboards = board->Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.BlackPawnFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.BlackKnightFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.BlackBishopFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.BlackRookFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, wFeaturePtr + *(whiteFeatureIndexes + Constants.BlackQueenFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }
    }

    private static void FullRefreshBlack(BoardStateData* board, int mirrored, int bucket, VectorShort* blackAcc)
    {

        Unsafe.CopyBlock(blackAcc, NnueWeights.FeatureBiases, L1ByteSize);



        var bFeaturePtr = NnueWeights.FeatureWeights + bucket * InputBucketWeightCount;
        var blackFeatureIndexes = NnueExtensions.BlackFeatureIndexes + mirrored;

        AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.WhiteKingFeatureIndexOffset + (board->WhiteKingSquare << 1)));

        var bitboards = board->Occupancy[Constants.WhitePawn];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.WhitePawnFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.WhiteKnightFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.WhiteBishopFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.WhiteRookFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.WhiteQueenFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.BlackKingFeatureIndexOffset + (board->BlackKingSquare << 1)));

        bitboards = board->Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.BlackPawnFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.BlackKnightFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.BlackBishopFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.BlackRookFeatureIndexOffset + (bitboards.PopLSB() << 1)));
        }

        bitboards = board->Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, bFeaturePtr + *(blackFeatureIndexes + Constants.BlackQueenFeatureIndexOffset + (bitboards.PopLSB() << 1)));
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

    public void RefreshAccumulatorWhite(BoardStateData* boards, AccumulatorState* accumulators, int depthFromRoot)
    {
        VectorShort* accumulator = WhiteAccumulators[depthFromRoot];

        accumulators->Evaluation = TranspositionTableExtensions.NoHashEntry;
        accumulators->WhiteNeedsRefresh = false;
        accumulators->WhiteMirrored = boards->WhiteKingSquare.IsMirroredSide();
        accumulators->WhiteInputBucket = *(NnueWeights.BucketLayout + boards->WhiteKingSquare);
        accumulators->WhiteAccumulatorUpToDate = true;

        var mirrored = accumulators->WhiteMirrored ? 1 : 0;
        var bucketCacheIndex = (accumulators->WhiteInputBucket<< 1) + mirrored;
        var cachedBoard = BucketCacheWhiteBoards + bucketCacheIndex;
        var cachedAccumulator = BucketCacheWhiteAccumulators[bucketCacheIndex];

        if (cachedBoard->PieceCount == 0)
        {
            FullRefreshWhite(boards, mirrored, accumulators->WhiteInputBucket, accumulator);
            Unsafe.CopyBlock(cachedAccumulator, accumulator, L1ByteSize);
            Unsafe.CopyBlock(cachedBoard, boards, BoardStateData.BoardStateSize);
            return;
        }

        var featureIndexes = NnueExtensions.WhiteFeatureIndexes + mirrored;
        var bucketOffset = NnueWeights.FeatureWeights + accumulators->WhiteInputBucket * InputBucketWeightCount;
        if (boards->BlackKingSquare != cachedBoard->BlackKingSquare)
        {
            var subKing = bucketOffset + *(featureIndexes + Constants.BlackKingFeatureIndexOffset + (cachedBoard->BlackKingSquare<< 1));
            var addKing = bucketOffset + *(featureIndexes + Constants.BlackKingFeatureIndexOffset + (boards->BlackKingSquare<< 1));

            SubAdd(cachedAccumulator, cachedAccumulator, subKing, addKing);

        }

        if (boards->WhiteKingSquare != cachedBoard->WhiteKingSquare)
        {
            var subKing = bucketOffset + *(featureIndexes + Constants.WhiteKingFeatureIndexOffset + (cachedBoard->WhiteKingSquare<< 1));
            var addKing = bucketOffset + *(featureIndexes + Constants.WhiteKingFeatureIndexOffset + (boards->WhiteKingSquare<< 1));
            SubAdd(cachedAccumulator, cachedAccumulator, subKing, addKing);
        }

        for (int i = Constants.BlackPawn; i < Constants.BlackKing; i++)
        {
            var prev = cachedBoard->Occupancy[i];
            var curr = boards->Occupancy[i];

            var added = curr & ~prev;
            var removed = prev & ~curr;

            while (added != 0)
            {
                AddWeights(cachedAccumulator, bucketOffset + *(featureIndexes + (i << 7) + (added.PopLSB() << 1)));
            }

            while (removed != 0)
            {
                Sub(cachedAccumulator, cachedAccumulator, bucketOffset + *(featureIndexes + (i << 7) + (removed.PopLSB() << 1)));
            }

        }

        Unsafe.CopyBlock(accumulator, cachedAccumulator, L1ByteSize);
        Unsafe.CopyBlock(cachedBoard, boards, BoardStateData.BoardStateSize);
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
        var bucketCacheIndex = (accumulatorState->BlackInputBucket<< 1) + mirrored;

        var cachedBoard = BucketCacheBlackBoards + bucketCacheIndex;
        var cachedAccumulator = BucketCacheBlackAccumulators[bucketCacheIndex];

        if (cachedBoard->PieceCount == 0)
        {
            FullRefreshBlack(board, mirrored, accumulatorState->BlackInputBucket, accumulator);
            Unsafe.CopyBlock(cachedAccumulator, accumulator, L1ByteSize);
            Unsafe.CopyBlock(cachedBoard, board, BoardStateData.BoardStateSize);
            return;
        }

        var featureIndexes = NnueExtensions.BlackFeatureIndexes + mirrored;
        var bucketOffset = NnueWeights.FeatureWeights + accumulatorState->BlackInputBucket * InputBucketWeightCount;
        if (board->BlackKingSquare != cachedBoard->BlackKingSquare)
        {
            var subKing = bucketOffset + *(featureIndexes + Constants.BlackKingFeatureIndexOffset + (cachedBoard->BlackKingSquare<< 1)); 
            var addKing = bucketOffset + *(featureIndexes + Constants.BlackKingFeatureIndexOffset + (board->BlackKingSquare<< 1));

            SubAdd(cachedAccumulator, cachedAccumulator, subKing, addKing);
        }

        if (board->WhiteKingSquare != cachedBoard->WhiteKingSquare)
        {
            var subKing = bucketOffset + *(featureIndexes + Constants.WhiteKingFeatureIndexOffset + (cachedBoard->WhiteKingSquare << 1));
            var addKing = bucketOffset + *(featureIndexes + Constants.WhiteKingFeatureIndexOffset + (board->WhiteKingSquare << 1));
            SubAdd(cachedAccumulator, cachedAccumulator, subKing, addKing);

        }

        for (int i = Constants.BlackPawn; i < Constants.BlackKing; i++)
        {
            var prev = cachedBoard->Occupancy[i];
            var curr = board->Occupancy[i];

            var added = curr & ~prev;
            var removed = prev & ~curr;

            while (added != 0)
            {
                AddWeights(cachedAccumulator, bucketOffset + *(featureIndexes + (i << 7) + (added.PopLSB() << 1)));
            }

            while (removed != 0)
            {
                Sub(cachedAccumulator, cachedAccumulator, bucketOffset + *(featureIndexes + (i << 7) + (removed.PopLSB() << 1)));
            }
        }

        Unsafe.CopyBlock(accumulator, cachedAccumulator, L1ByteSize);
        Unsafe.CopyBlock(cachedBoard, board, BoardStateData.BoardStateSize);
    }
}