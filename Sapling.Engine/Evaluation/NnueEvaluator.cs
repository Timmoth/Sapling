using System.Runtime.CompilerServices;

namespace Sapling.Engine.Evaluation;

public static unsafe partial class NnueEvaluator
{
#if AVX512
            const int VectorSize = 32; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#else
    private const int VectorSize = 16; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#endif

    private const int Scale = 400;
    private const int Q = 255 * 64;

    private const int ColorStride = 64 * 6;
    private const int PieceStride = 64;

    private static readonly VectorShort Ceil = VectorType.Create<short>(255);
    private static readonly VectorShort Floor = VectorType.Create<short>(0);

    public const int AccumulatorSize = NnueWeights.Layer1Size / VectorSize;

    public const int L1ByteSize = sizeof(short) * NnueWeights.Layer1Size;
    public const int InputBucketWeightCount = NnueWeights.InputSize * AccumulatorSize;

    private const int BucketDivisor = (32 + NnueWeights.OutputBuckets - 1) / NnueWeights.OutputBuckets;

    public static int Evaluate(BoardStateEntry* searchStack, BucketCache* bucketCache, int depthFromRoot)
    {
        ref var board = ref searchStack[depthFromRoot];
        ref var accumulatorState = ref board.AccumulatorState;

        if (accumulatorState.Evaluation.HasValue)
        {
            return accumulatorState.Evaluation.Value;
        }

        UpdateWhiteAccumulator(searchStack, bucketCache, depthFromRoot);
        UpdateBlackAccumulator(searchStack, bucketCache, depthFromRoot);

        var bucket = (board.Data.PieceCount - 2) / BucketDivisor;

#if AVX512
        var output = board.Data.WhiteToMove
            ? ForwardCReLU512(board.WhiteAccumulator, board.BlackAccumulator, bucket)
            : ForwardCReLU512(board.BlackAccumulator, board.WhiteAccumulator, bucket);
#else
        var output = board.Data.WhiteToMove
            ? ForwardCReLU256(board.WhiteAccumulator, board.BlackAccumulator, bucket)
            : ForwardCReLU256(board.BlackAccumulator, board.WhiteAccumulator, bucket);

#endif

        var finalEval = (output + NnueWeights.OutputBiases[bucket]) * Scale / Q;
        accumulatorState.Evaluation = finalEval;

        return finalEval;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FeatureIndices(bool whiteMirrored, bool blackMirrored, int piece, int square, out int wIndex, out int bIndex)
    {
        var whitePieceSquare = whiteMirrored ? square ^ 7 : square;
        var blackPieceSquare = blackMirrored ? square ^ 0x38 ^ 7 : square ^ 0x38;

        var white = piece & 1 ^ 1;
        var typeStride = ((piece >> 1) - white)* PieceStride;

        bIndex = white * ColorStride + typeStride + blackPieceSquare;
        wIndex = (white ^ 1) * ColorStride + typeStride + whitePieceSquare;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WhiteFeatureIndices(bool mirrored, int piece, byte square)
    {
        if (mirrored)
        {
            square ^= 7;
        }

        var white = piece & 1 ^ 1;
        var type = (piece >> 1) - white;

        return (white ^ 1) * ColorStride + type * PieceStride + square;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BlackFeatureIndices(bool mirrored, int piece, byte square)
    {
        square ^= 0x38;

        if (mirrored)
        {
            square ^= 7;
        }

        var white = piece & 1 ^ 1;

        var type = (piece >> 1) - white;
        return white * ColorStride + type * PieceStride + square;
    }

    public static void FillAccumulators(this ref BoardStateEntry entry)
    {
        ref BoardStateData board = ref entry.Data;
        ref AccumulatorState accumulatorState = ref entry.AccumulatorState;
        VectorShort* whiteAcc = entry.WhiteAccumulator;
        VectorShort* blackAcc = entry.BlackAccumulator;

        accumulatorState.UpdateTo(ref board);
        accumulatorState.WhiteAccumulatorUpToDate = accumulatorState.BlackAccumulatorUpToDate = true;

#if AVX512
        SimdResetAccumulators512(whiteAcc, blackAcc);
#else
        SimdResetAccumulators256(whiteAcc, blackAcc);
#endif
        var whiteMirrored = accumulatorState.WhiteMirrored;
        var blackMirrored = accumulatorState.BlackMirrored;

        var wFeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bFeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackInputBucket * InputBucketWeightCount;

        FeatureIndices(whiteMirrored, blackMirrored, Constants.WhiteKing, board.WhiteKingSquare, out var wIdx, out var bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
        AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif

        var bitboard = board.Occupancy[Constants.WhitePawn];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.WhitePawn, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

        bitboard = board.Occupancy[Constants.WhiteKnight];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.WhiteKnight, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

        bitboard = board.Occupancy[Constants.WhiteBishop];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.WhiteBishop, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

        bitboard = board.Occupancy[Constants.WhiteRook];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.WhiteRook, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

        bitboard = board.Occupancy[Constants.WhiteQueen];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.WhiteQueen, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

        FeatureIndices(whiteMirrored, blackMirrored, Constants.BlackKing, board.BlackKingSquare, out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
        AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif

        bitboard = board.Occupancy[Constants.BlackPawn];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.BlackPawn, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

        bitboard = board.Occupancy[Constants.BlackKnight];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.BlackKnight, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

        bitboard = board.Occupancy[Constants.BlackBishop];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.BlackBishop, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

        bitboard = board.Occupancy[Constants.BlackRook];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.BlackRook, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

        bitboard = board.Occupancy[Constants.BlackQueen];
        while (bitboard != 0)
        {
            FeatureIndices(whiteMirrored, blackMirrored, Constants.BlackQueen, bitboard.PopLSB(), out wIdx, out bIdx);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }
    }

    
    private static void FullRefreshWhite(this ref BoardStateData board, bool mirrored, int bucket, VectorShort* whiteAcc)
    {
#if AVX512
        SimdCopy512(whiteAcc, NnueWeights.FeatureBiases);
#else
        SimdCopy256(whiteAcc, NnueWeights.FeatureBiases);
#endif


        var wFeaturePtr = NnueWeights.FeatureWeights + bucket * InputBucketWeightCount;
        var wIdx = WhiteFeatureIndices(mirrored, Constants.WhiteKing, board.WhiteKingSquare);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
        AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif

        var bitboards = board.Occupancy[Constants.WhitePawn];

        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.WhitePawn, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }

        bitboards = board.Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.WhiteKnight, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }

        bitboards = board.Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.WhiteBishop, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }

        bitboards = board.Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.WhiteRook, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }

        bitboards = board.Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.WhiteQueen, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }

        wIdx = WhiteFeatureIndices(mirrored, Constants.BlackKing, board.BlackKingSquare);
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
        AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif

        bitboards = board.Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.BlackPawn, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }

        bitboards = board.Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.BlackKnight, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }

        bitboards = board.Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.BlackBishop, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }

        bitboards = board.Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.BlackRook, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }

        bitboards = board.Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            wIdx = WhiteFeatureIndices(mirrored, Constants.BlackQueen, bitboards.PopLSB());
#if AVX512
        AddWeights512(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#else
            AddWeights256(whiteAcc, wFeaturePtr + wIdx * AccumulatorSize);
#endif
        }
    }

    private static void FullRefreshBlack(this ref BoardStateData board, bool mirrored, int bucket, VectorShort* blackAcc)
    {

#if AVX512
        SimdCopy512(blackAcc, NnueWeights.FeatureBiases);
#else
        SimdCopy256(blackAcc, NnueWeights.FeatureBiases);
#endif

        var bFeaturePtr = NnueWeights.FeatureWeights + bucket * InputBucketWeightCount;
        var bIdx = BlackFeatureIndices(mirrored, Constants.WhiteKing, board.WhiteKingSquare);
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
        AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        var bitboards = board.Occupancy[Constants.WhitePawn];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.WhitePawn, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

            bitboards = board.Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.WhiteKnight, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

            bitboards = board.Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.WhiteBishop, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

            bitboards = board.Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.WhiteRook, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

            bitboards = board.Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.WhiteQueen, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

            bIdx = BlackFeatureIndices(mirrored, Constants.BlackKing, board.BlackKingSquare);
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif

        bitboards = board.Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.BlackPawn, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

            bitboards = board.Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.BlackKnight, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

            bitboards = board.Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.BlackBishop, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

            bitboards = board.Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.BlackRook, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }

            bitboards = board.Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            bIdx = BlackFeatureIndices(mirrored, Constants.BlackQueen, bitboards.PopLSB());
#if AVX512
        AddWeights512(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#else
            AddWeights256(blackAcc, bFeaturePtr + bIdx * AccumulatorSize);
#endif
        }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyQuiet(this ref AccumulatorState accumulatorState, byte fromPiece, byte fromSquare,
        byte toPiece, byte toSquare)
    {
        var whiteMirrored = accumulatorState.WhiteMirrored;
        var blackMirrored = accumulatorState.BlackMirrored;

        FeatureIndices(whiteMirrored, blackMirrored, fromPiece, fromSquare, out var fromWIndex, out var fromBIndex);
        FeatureIndices(whiteMirrored, blackMirrored, toPiece, toSquare, out var toWIndex, out var toBIndex);

        var wBucketOffset = accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bBucketOffset = accumulatorState.BlackInputBucket * InputBucketWeightCount;

        var fromWFeatureUpdate = wBucketOffset + fromWIndex * AccumulatorSize;
        var fromBFeatureUpdate = bBucketOffset + fromBIndex * AccumulatorSize;
        var toWFeatureUpdate = wBucketOffset + toWIndex * AccumulatorSize;
        var toBFeatureUpdate = bBucketOffset + toBIndex * AccumulatorSize;

        accumulatorState.WhiteSubFeatureUpdates[0] = fromWFeatureUpdate;
        accumulatorState.BlackSubFeatureUpdates[0] = fromBFeatureUpdate;
        accumulatorState.WhiteAddFeatureUpdates[0] = toWFeatureUpdate;
        accumulatorState.BlackAddFeatureUpdates[0] = toBFeatureUpdate;

        accumulatorState.ChangeType = AccumulatorChangeType.SubAdd;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyCapture(this ref AccumulatorState accumulatorState,
     byte fromPiece, byte fromSquare,
     byte toPiece, byte toSquare,
     byte capturedPiece, byte capturedSquare)
    {
        var whiteMirrored = accumulatorState.WhiteMirrored;
        var blackMirrored = accumulatorState.BlackMirrored;
        
        FeatureIndices(whiteMirrored, blackMirrored, fromPiece, fromSquare, out var fromWIndex, out var fromBIndex);
        FeatureIndices(whiteMirrored, blackMirrored, toPiece, toSquare, out var toWIndex, out var toBIndex);
        FeatureIndices(whiteMirrored, blackMirrored, capturedPiece, capturedSquare, out var capWIndex, out var capBIndex);

        var wBucketOffset = accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bBucketOffset = accumulatorState.BlackInputBucket * InputBucketWeightCount;

        var fromWFeatureUpdate = wBucketOffset + fromWIndex * AccumulatorSize;
        var fromBFeatureUpdate = bBucketOffset + fromBIndex * AccumulatorSize;
        var capWFeatureUpdate = wBucketOffset + capWIndex * AccumulatorSize;
        var capBFeatureUpdate = bBucketOffset + capBIndex * AccumulatorSize;
        var toWFeatureUpdate = wBucketOffset + toWIndex * AccumulatorSize;
        var toBFeatureUpdate = bBucketOffset + toBIndex * AccumulatorSize;
        accumulatorState.WhiteSubFeatureUpdates[0] = fromWFeatureUpdate;
        accumulatorState.BlackSubFeatureUpdates[0] = fromBFeatureUpdate;
        accumulatorState.WhiteSubFeatureUpdates[1] = capWFeatureUpdate;
        accumulatorState.BlackSubFeatureUpdates[1] = capBFeatureUpdate;
        accumulatorState.WhiteAddFeatureUpdates[0] = toWFeatureUpdate;
        accumulatorState.BlackAddFeatureUpdates[0] = toBFeatureUpdate;
        accumulatorState.ChangeType = AccumulatorChangeType.SubSubAdd;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyCastle(this ref AccumulatorState accumulatorState,
        byte kingPiece, byte fromKingSquare, byte toKingSquare,
      byte rookPiece, byte fromRookSquare, byte toRookSquare)
    {
        var whiteMirrored = accumulatorState.WhiteMirrored;
        var blackMirrored = accumulatorState.BlackMirrored;

        FeatureIndices(whiteMirrored, blackMirrored, kingPiece, fromKingSquare, out var fromKingWIndex, out var fromKingBIndex);
        FeatureIndices(whiteMirrored, blackMirrored, kingPiece, toKingSquare, out var toKingWIndex, out var toKingBIndex);
        FeatureIndices(whiteMirrored, blackMirrored, rookPiece, fromRookSquare, out var fromRookWIndex, out var fromRookBIndex);
        FeatureIndices(whiteMirrored, blackMirrored, rookPiece, toRookSquare, out var toRookWIndex, out var toRookBIndex);

        var wBucketOffset = accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bBucketOffset = accumulatorState.BlackInputBucket * InputBucketWeightCount;

        var fromKingWFeatureUpdate = wBucketOffset + fromKingWIndex * AccumulatorSize;
        var fromKingBFeatureUpdate = bBucketOffset + fromKingBIndex * AccumulatorSize;
        var toKingWFeatureUpdate = wBucketOffset + toKingWIndex * AccumulatorSize;
        var toKingBFeatureUpdate = bBucketOffset + toKingBIndex * AccumulatorSize;
        var fromRookWFeatureUpdate = wBucketOffset + fromRookWIndex * AccumulatorSize;
        var fromRookBFeatureUpdate = bBucketOffset + fromRookBIndex * AccumulatorSize;
        var toRookWFeatureUpdate = wBucketOffset + toRookWIndex * AccumulatorSize;
        var toRookBFeatureUpdate = bBucketOffset + toRookBIndex * AccumulatorSize;

        // Apply the pre-calculated values to the feature update arrays
        accumulatorState.WhiteSubFeatureUpdates[0] = fromKingWFeatureUpdate;
        accumulatorState.BlackSubFeatureUpdates[0] = fromKingBFeatureUpdate;
        accumulatorState.WhiteSubFeatureUpdates[1] = fromRookWFeatureUpdate;
        accumulatorState.BlackSubFeatureUpdates[1] = fromRookBFeatureUpdate;

        accumulatorState.WhiteAddFeatureUpdates[0] = toKingWFeatureUpdate;
        accumulatorState.BlackAddFeatureUpdates[0] = toKingBFeatureUpdate;
        accumulatorState.WhiteAddFeatureUpdates[1] = toRookWFeatureUpdate;
        accumulatorState.BlackAddFeatureUpdates[1] = toRookBFeatureUpdate;

        // Set the change type
        accumulatorState.ChangeType = AccumulatorChangeType.SubSubAddAdd;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateWhiteAccumulator(BoardStateEntry* searchStack, BucketCache* bucketCache, int depthFromRoot)
    {
        // Check if current depth is already up-to-date or needs refresh.
        ref var currentState = ref searchStack[depthFromRoot].AccumulatorState;

        if (currentState.WhiteAccumulatorUpToDate)
        {
            return;
        }

        if (currentState.WhiteNeedsRefresh)
        {
            searchStack[depthFromRoot].RefreshAccumulatorWhite(bucketCache);
            return;
        }

        // Find the nearest up-to-date or needs-refresh state moving upwards.
        var refreshDepth = depthFromRoot - 1;
        while (refreshDepth >= 0)
        {
            ref var parentState = ref searchStack[refreshDepth].AccumulatorState;

            if (parentState.WhiteAccumulatorUpToDate)
            {
                refreshDepth++;
                break;
            }

            if (parentState.WhiteNeedsRefresh)
            {
                searchStack[refreshDepth].RefreshAccumulatorWhite(bucketCache);
                refreshDepth++;
                break;
            }

            refreshDepth--;
        }

        // Apply updates downwards to the current depth.
        for (var i = refreshDepth; i <= depthFromRoot; i++)
        {
            ApplyWhiteUpdate(
                searchStack[i - 1].WhiteAccumulator,
                searchStack[i].WhiteAccumulator,
                ref searchStack[i].AccumulatorState
            );
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateBlackAccumulator(BoardStateEntry* searchStack, BucketCache* bucketCache, int depthFromRoot)
    {
        // Check if current depth is already up-to-date or needs refresh.
        ref var currentState = ref searchStack[depthFromRoot].AccumulatorState;

        if (currentState.BlackAccumulatorUpToDate)
        {
            return;
        }

        if (currentState.BlackNeedsRefresh)
        {
            searchStack[depthFromRoot].RefreshAccumulatorBlack(bucketCache);
            return;
        }

        // Find the nearest up-to-date or needs-refresh state moving upwards.
        var refreshDepth = depthFromRoot - 1;
        while (refreshDepth >= 0)
        {
            ref var parentState = ref searchStack[refreshDepth].AccumulatorState;

            if (parentState.BlackAccumulatorUpToDate)
            {
                refreshDepth++;
                break;
            }

            if (parentState.BlackNeedsRefresh)
            {
                searchStack[refreshDepth].RefreshAccumulatorBlack(bucketCache);
                refreshDepth++;
                break;
            }

            refreshDepth--;
        }

        // Apply updates downwards to the current depth.
        for (var i = refreshDepth; i <= depthFromRoot; i++)
        {
            ApplyBlackUpdate(
                searchStack[i - 1].BlackAccumulator,
                searchStack[i].BlackAccumulator,
                ref searchStack[i].AccumulatorState
            );
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyWhiteUpdate(VectorShort* prevAccumulator, VectorShort* accumulator, ref AccumulatorState accumulatorState)
    {
        if (accumulatorState.ChangeType == AccumulatorChangeType.SubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteSubFeatureUpdates[0];
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteAddFeatureUpdates[0];

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
        else if (accumulatorState.ChangeType == AccumulatorChangeType.SubSubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteSubFeatureUpdates[0];
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteSubFeatureUpdates[1];
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteAddFeatureUpdates[0];

#if AVX512
            SubSubAdd512(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);
#else
            SubSubAdd256(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);
#endif

        }
        else if (accumulatorState.ChangeType == AccumulatorChangeType.SubSubAddAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteSubFeatureUpdates[0];
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteSubFeatureUpdates[1];
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteAddFeatureUpdates[0];
            var add2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.WhiteAddFeatureUpdates[1];

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
        else if (accumulatorState.ChangeType == AccumulatorChangeType.None)
        {

#if AVX512
            SimdCopy512(accumulator, prevAccumulator);
#else
            SimdCopy256(accumulator, prevAccumulator);
#endif

        }

        accumulatorState.WhiteAccumulatorUpToDate = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyBlackUpdate(VectorShort* prevAccumulator, VectorShort* accumulator, ref AccumulatorState accumulatorState)
    {
        if (accumulatorState.ChangeType == AccumulatorChangeType.SubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackSubFeatureUpdates[0];
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackAddFeatureUpdates[0];

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
        else if (accumulatorState.ChangeType == AccumulatorChangeType.SubSubAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackSubFeatureUpdates[0];
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackSubFeatureUpdates[1];
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackAddFeatureUpdates[0];

#if AVX512
            SubSubAdd512(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);

#else
            SubSubAdd256(prevAccumulator, accumulator, sub1FeaturePtr, sub2FeaturePtr, add1FeaturePtr);
#endif

        }
        else if (accumulatorState.ChangeType == AccumulatorChangeType.SubSubAddAdd)
        {
            var sub1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackSubFeatureUpdates[0];
            var sub2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackSubFeatureUpdates[1];
            var add1FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackAddFeatureUpdates[0];
            var add2FeaturePtr = NnueWeights.FeatureWeights + accumulatorState.BlackAddFeatureUpdates[1];

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
        else if (accumulatorState.ChangeType == AccumulatorChangeType.None)
        {
#if AVX512
            SimdCopy512(accumulator, prevAccumulator);
#else
            SimdCopy256(accumulator, prevAccumulator);
#endif
        }

        accumulatorState.BlackAccumulatorUpToDate = true;
    }

    public static void RefreshAccumulatorWhite(this ref BoardStateEntry entry, BucketCache* bucketCache)
    {
        ref BoardStateData board = ref entry.Data;
        ref AccumulatorState accumulatorState = ref entry.AccumulatorState;
        VectorShort* accumulator = entry.WhiteAccumulator;

        accumulatorState.Evaluation = null;
        accumulatorState.WhiteNeedsRefresh = false;
        accumulatorState.WhiteMirrored = board.WhiteKingSquare.IsMirroredSide();
        accumulatorState.WhiteInputBucket = NnueWeights.BucketLayout[board.WhiteKingSquare];
        accumulatorState.WhiteAccumulatorUpToDate = true;

        var bucketCacheIndex = accumulatorState.WhiteInputBucket * 2 + (accumulatorState.WhiteMirrored ? 0 : 1);
        ref var cachedBoard = ref bucketCache[bucketCacheIndex].WhiteBoard;
        var cachedAccumulator = bucketCache[bucketCacheIndex].WhiteAccumulator;

        var mirrored = accumulatorState.WhiteMirrored;
        if (cachedBoard.PieceCount == 0)
        {
            FullRefreshWhite(ref board, mirrored, accumulatorState.WhiteInputBucket, cachedAccumulator);
#if AVX512
            SimdCopy512(accumulator, cachedAccumulator);
#else
            SimdCopy256(accumulator, cachedAccumulator);
#endif

            board.CloneTo(ref cachedBoard);
            return;
        }

        var bucketOffset = NnueWeights.FeatureWeights + accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        if (board.BlackKingSquare != cachedBoard.BlackKingSquare)
        {
            var subKing = bucketOffset + WhiteFeatureIndices(mirrored, Constants.BlackKing, cachedBoard.BlackKingSquare) * AccumulatorSize;
            var addKing = bucketOffset + WhiteFeatureIndices(mirrored, Constants.BlackKing, board.BlackKingSquare) * AccumulatorSize;

#if AVX512
            SubAdd512(cachedAccumulator, cachedAccumulator, subKing, addKing);
#else
            SubAdd256(cachedAccumulator, cachedAccumulator, subKing, addKing);
#endif

        }

        if (board.WhiteKingSquare != cachedBoard.WhiteKingSquare)
        {
            var subKing = bucketOffset + WhiteFeatureIndices(mirrored,Constants.WhiteKing, cachedBoard.WhiteKingSquare) * AccumulatorSize;
            var addKing = bucketOffset + WhiteFeatureIndices(mirrored, Constants.WhiteKing, board.WhiteKingSquare) * AccumulatorSize;

#if AVX512
            SubAdd512(cachedAccumulator, cachedAccumulator, subKing, addKing);
#else
            SubAdd256(cachedAccumulator, cachedAccumulator, subKing, addKing);
#endif
        }

            for (int i = Constants.BlackPawn; i < Constants.BlackKing; i++)
        {
            var prev = cachedBoard.Occupancy[i];
            var curr = board.Occupancy[i];

            var added = curr & ~prev;
            var removed = prev & ~curr;

            while (added != 0)
            {
                var idx = WhiteFeatureIndices(mirrored, i, added.PopLSB());
#if AVX512
                AddWeights512(cachedAccumulator, bucketOffset + idx * AccumulatorSize);
#else
                AddWeights256(cachedAccumulator, bucketOffset + idx * AccumulatorSize);
#endif
            }

            while (removed != 0)
            {
                var idx = WhiteFeatureIndices(mirrored, i, removed.PopLSB());
#if AVX512
                Sub512(cachedAccumulator, cachedAccumulator, bucketOffset + idx * AccumulatorSize);
#else
                Sub256(cachedAccumulator, cachedAccumulator, bucketOffset + idx * AccumulatorSize);
#endif
            }

        }

#if AVX512
        SimdCopy512(accumulator, cachedAccumulator);
#else
        SimdCopy256(accumulator, cachedAccumulator);
#endif
        board.CloneTo(ref cachedBoard);
    }


    public static void RefreshAccumulatorBlack(this ref BoardStateEntry entry, BucketCache* bucketCache)
    {
        ref BoardStateData board = ref entry.Data;
        ref AccumulatorState accumulatorState = ref entry.AccumulatorState;
        VectorShort* accumulator = entry.BlackAccumulator;

        accumulatorState.Evaluation = null;
        accumulatorState.BlackNeedsRefresh = false;
        accumulatorState.BlackMirrored = board.BlackKingSquare.IsMirroredSide();
        accumulatorState.BlackInputBucket = NnueWeights.BucketLayout[board.BlackKingSquare ^ 0x38];
        accumulatorState.BlackAccumulatorUpToDate = true;

        var bucketCacheIndex = accumulatorState.BlackInputBucket * 2 + (accumulatorState.BlackMirrored ? 0 : 1);
        ref var cachedBoard = ref bucketCache[bucketCacheIndex].BlackBoard;
        var cachedAccumulator = bucketCache[bucketCacheIndex].BlackAccumulator;
        var mirrored = accumulatorState.BlackMirrored;

        if (cachedBoard.PieceCount == 0)
        {
            FullRefreshBlack(ref board, mirrored, accumulatorState.BlackInputBucket, cachedAccumulator);
#if AVX512
            SimdCopy512(accumulator, cachedAccumulator);
#else
            SimdCopy256(accumulator, cachedAccumulator);
#endif
            board.CloneTo(ref cachedBoard);
            return;
        }

        var bucketOffset = NnueWeights.FeatureWeights + accumulatorState.BlackInputBucket * InputBucketWeightCount;

        if (board.BlackKingSquare != cachedBoard.BlackKingSquare)
        {
            var subKing = bucketOffset + BlackFeatureIndices(mirrored, Constants.BlackKing, cachedBoard.BlackKingSquare) * AccumulatorSize;
            var addKing = bucketOffset + BlackFeatureIndices(mirrored, Constants.BlackKing, board.BlackKingSquare) * AccumulatorSize;

#if AVX512
            SubAdd512(cachedAccumulator, cachedAccumulator, subKing, addKing);
#else
            SubAdd256(cachedAccumulator, cachedAccumulator, subKing, addKing);
#endif
        }

        if (board.WhiteKingSquare != cachedBoard.WhiteKingSquare)
        {
            var subKing = bucketOffset + BlackFeatureIndices(mirrored, Constants.WhiteKing, cachedBoard.WhiteKingSquare) * AccumulatorSize;
            var addKing = bucketOffset + BlackFeatureIndices(mirrored, Constants.WhiteKing, board.WhiteKingSquare) * AccumulatorSize;
#if AVX512
            SubAdd512(cachedAccumulator, cachedAccumulator, subKing, addKing);
#else
            SubAdd256(cachedAccumulator, cachedAccumulator, subKing, addKing);
#endif
        }

        for (int i = Constants.BlackPawn; i < Constants.BlackKing; i++)
        {
            var prev = cachedBoard.Occupancy[i];
            var curr = board.Occupancy[i];

            var added = curr & ~prev;
            var removed = prev & ~curr;

            while (added != 0)
            {
                var idx = BlackFeatureIndices(mirrored, i, added.PopLSB());
#if AVX512
                AddWeights512(cachedAccumulator, bucketOffset + idx * AccumulatorSize);
#else
                AddWeights256(cachedAccumulator, bucketOffset + idx * AccumulatorSize);
#endif
            }

            while (removed != 0)
            {
                var idx = BlackFeatureIndices(mirrored, i, removed.PopLSB());
#if AVX512
                Sub512(cachedAccumulator, cachedAccumulator, bucketOffset + idx * AccumulatorSize);
#else
                Sub256(cachedAccumulator, cachedAccumulator, bucketOffset + idx * AccumulatorSize);
#endif
            }
        }

#if AVX512
        SimdCopy512(accumulator, cachedAccumulator);
#else
        SimdCopy256(accumulator, cachedAccumulator);
#endif
        board.CloneTo(ref cachedBoard);
    }
}