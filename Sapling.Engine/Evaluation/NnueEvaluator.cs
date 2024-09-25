using System.Runtime.CompilerServices;

namespace Sapling.Engine.Evaluation;

public static unsafe class NnueEvaluator
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SimdCopy(VectorShort* destination, VectorShort* source)
    {
        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            destination[i] = source[i];
        }
    }

    private const int BucketDivisor = (32 + NnueWeights.OutputBuckets - 1) / NnueWeights.OutputBuckets;

    public static int Evaluate(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc)
    {
        if (board.WhiteNeedsRefresh)
        {
            board.RefreshWhite(whiteAcc);
        }

        if (board.BlackNeedsRefresh)
        {
            board.RefreshBlack(blackAcc);
        }

        var bucket = (board.PieceCount - 2) / BucketDivisor;

        var output = board.WhiteToMove
            ? ForwardCReLU(whiteAcc, blackAcc, bucket)
            : ForwardCReLU(blackAcc, whiteAcc, bucket);

        return (output + NnueWeights.OutputBiases[bucket]) * Scale / Q;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int blackIdx, int whiteIdx) FeatureIndices(bool whiteMirrored, bool blackMirrored, int piece, int square)
    {
        var whitePieceSquare = whiteMirrored ? square ^ 7 : square;
        var blackPieceSquare = blackMirrored ? square ^ 0x38 ^ 7 : square ^ 0x38;

        var white = (piece + 1) % 2;
        var type = (piece >> 1) - white;

        return (white * ColorStride + type * PieceStride + blackPieceSquare,
            (white ^ 1) * ColorStride + type * PieceStride + whitePieceSquare);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WhiteFeatureIndices(bool mirrored, int piece, byte square)
    {
        if (mirrored)
        {
            square ^= 7;
        }

        var white = (piece + 1) % 2;
        var type = (piece >> 1) - white;

        return (white ^ 1) * ColorStride + type * PieceStride + square;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BlackFeatureIndices(bool mirrored, int piece, byte square)
    {
        var blackPieceSquare = mirrored ? square ^ 0x38 ^ 7 : square ^ 0x38;

        var white = (piece + 1) % 2;
        var type = (piece >> 1) - white;

        return white * ColorStride + type * PieceStride + blackPieceSquare;
    }

    public static void ApplyQuiet(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc,
        int fromPiece, int fromSquare, 
        int toPiece, int toSquare)
    {
        var (fromBIndex, fromWIndex) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, fromPiece, fromSquare);
        var (toBIndex, toWIndex) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, toPiece, toSquare);

        if (!board.WhiteNeedsRefresh)
        {
            var featurePtr = NnueWeights.FeatureWeights + board.WhiteInputBucket * InputBucketWeightCount;

            var addFeaturePtr = featurePtr + toWIndex * AccumulatorSize;
            var subFeaturePtr = featurePtr + fromWIndex * AccumulatorSize;

            for (var i = AccumulatorSize - 1; i >= 0; i--)
            {
                whiteAcc[i] =
                    AvxIntrinsics.Add(whiteAcc[i], AvxIntrinsics.Subtract(addFeaturePtr[i], subFeaturePtr[i]));
            }
        }

        if (!board.BlackNeedsRefresh)
        {
            var featurePtr = NnueWeights.FeatureWeights + board.BlackInputBucket * InputBucketWeightCount;

            var addFeaturePtr = featurePtr + toBIndex * AccumulatorSize;
            var subFeaturePtr = featurePtr + fromBIndex * AccumulatorSize;

            for (var i = AccumulatorSize - 1; i >= 0; i--)
            {
                blackAcc[i] =
                    AvxIntrinsics.Add(blackAcc[i], AvxIntrinsics.Subtract(addFeaturePtr[i], subFeaturePtr[i]));
            }
        }
    }

    public static void ApplyCapture(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc,
        int fromPiece, int fromSquare,
        int toPiece, int toSquare,
        int capturedPiece, int capturedSquare
        )
    {
        var (fromBIndex, fromWIndex) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, fromPiece, fromSquare);
        var (toBIndex, toWIndex) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, toPiece, toSquare);
        var (capBIndex, capWindex) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, capturedPiece, capturedSquare);
        if (!board.WhiteNeedsRefresh)
        {
            var featurePtr = NnueWeights.FeatureWeights + board.WhiteInputBucket * InputBucketWeightCount;

            var addFeaturePtr = featurePtr + toWIndex * AccumulatorSize;
            var subFeaturePtr = featurePtr + fromWIndex * AccumulatorSize;
            var sub2FeaturePtr = featurePtr + capWindex * AccumulatorSize;

            for (var i = AccumulatorSize - 1; i >= 0; i--)
            {
                whiteAcc[i] = AvxIntrinsics.Add(whiteAcc[i], AvxIntrinsics.Subtract(addFeaturePtr[i],
                    AvxIntrinsics.Add(subFeaturePtr[i], sub2FeaturePtr[i])));
            }
        }

        if (!board.BlackNeedsRefresh)
        {
            var featurePtr = NnueWeights.FeatureWeights + board.BlackInputBucket * InputBucketWeightCount;

            var addFeaturePtr = featurePtr + toBIndex * AccumulatorSize;
            var subFeaturePtr = featurePtr + fromBIndex * AccumulatorSize;
            var sub2FeaturePtr = featurePtr + capBIndex * AccumulatorSize;

            for (var i = AccumulatorSize - 1; i >= 0; i--)
            {
                blackAcc[i] = AvxIntrinsics.Add(blackAcc[i], AvxIntrinsics.Subtract(addFeaturePtr[i],
                    AvxIntrinsics.Add(subFeaturePtr[i], sub2FeaturePtr[i])));
            }
        }
    }

    public static void ApplyCastle(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc, 
        int kingPiece, int fromKingSquare, int toKingSquare,
        int rookPiece, int fromRookSquare, int toRookSquare)
    {
        var (fromKingBIndex, fromKingWIndex) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, kingPiece, fromKingSquare);
        var (toKingBIndex, toKingWIndex) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, kingPiece, toKingSquare);
        var (fromRookBIndex, fromRookWIndex) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, rookPiece, fromRookSquare);
        var (toRookBIndex, toRookWIndex) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, rookPiece, toRookSquare);

        if (!board.WhiteNeedsRefresh)
        {
            var featurePtr = NnueWeights.FeatureWeights + board.WhiteInputBucket * InputBucketWeightCount;

            var addFeaturePtr = featurePtr + toKingWIndex * AccumulatorSize;
            var subFeaturePtr = featurePtr + fromKingWIndex * AccumulatorSize;
            var add2FeaturePtr = featurePtr + toRookWIndex * AccumulatorSize;
            var sub2FeaturePtr = featurePtr + fromRookWIndex * AccumulatorSize;

            for (var i = AccumulatorSize - 1; i >= 0; i--)
            {
                whiteAcc[i] = AvxIntrinsics.Add(whiteAcc[i], AvxIntrinsics.Subtract(
                    AvxIntrinsics.Add(addFeaturePtr[i], add2FeaturePtr[i]),
                    AvxIntrinsics.Add(subFeaturePtr[i], sub2FeaturePtr[i])));
            }
        }

        if (!board.BlackNeedsRefresh)
        {
            var featurePtr = NnueWeights.FeatureWeights + board.BlackInputBucket * InputBucketWeightCount;

            var addFeaturePtr = featurePtr + toKingBIndex * AccumulatorSize;
            var subFeaturePtr = featurePtr + fromKingBIndex * AccumulatorSize;
            var add2FeaturePtr = featurePtr + toRookBIndex * AccumulatorSize;
            var sub2FeaturePtr = featurePtr + fromRookBIndex * AccumulatorSize;

            for (var i = AccumulatorSize - 1; i >= 0; i--)
            {
                blackAcc[i] = AvxIntrinsics.Add(blackAcc[i], AvxIntrinsics.Subtract(
                    AvxIntrinsics.Add(addFeaturePtr[i], add2FeaturePtr[i]),
                    AvxIntrinsics.Add(subFeaturePtr[i], sub2FeaturePtr[i])));
            }
        }
    }
    public static void Apply(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc, int piece, int square)
    {
        var (bIdx, wIdx) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, piece, square);
        if (!board.WhiteNeedsRefresh)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket, wIdx);
        }

        if (!board.BlackNeedsRefresh)
        {
            AddWeights(blackAcc, board.BlackInputBucket, bIdx);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddWeights(VectorShort* accuPtr, int inputBucket, int inputFeatureIndex)
    {
        var featurePtr = NnueWeights.FeatureWeights + inputBucket * InputBucketWeightCount + inputFeatureIndex * AccumulatorSize;
        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            accuPtr[i] = AvxIntrinsics.Add(accuPtr[i], featurePtr[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetKingBucket(byte index)
    {
        if (index < 2) // Indices 0-1 -> 0
            return 0;
        if (index < 6) // Indices 2-5 -> 1
            return 1;
        if (index < 8) // Indices 6-7 -> 0
            return 0;
        if (index < 16) // Indices 8-15 -> 2
            return 2;

        // Indices 16-63 -> 3
        return 3;
    }

    public static void FillAccumulators(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc)
    {
        board.WhiteNeedsRefresh = false;
        board.BlackNeedsRefresh = false;
        board.WhiteMirrored = board.WhiteKingSquare.IsMirroredSide();
        board.WhiteInputBucket = GetKingBucket(board.WhiteKingSquare);
        board.BlackMirrored = board.BlackKingSquare.IsMirroredSide();
        board.BlackInputBucket = GetKingBucket((byte)(board.BlackKingSquare ^ 0x38));

        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            whiteAcc[i] = blackAcc[i] = NnueWeights.FeatureBiases[i];
        }

        // Accumulate layer weights
        board.Apply(whiteAcc, blackAcc, Constants.WhiteKing, board.WhiteKingSquare);

        var bitboard = board.Occupancy[Constants.WhitePawn];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.WhitePawn, bitboard.PopLSB());
        }

        bitboard = board.Occupancy[Constants.WhiteKnight];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.WhiteKnight, bitboard.PopLSB());
        }

        bitboard = board.Occupancy[Constants.WhiteBishop];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.WhiteBishop, bitboard.PopLSB());
        }

        bitboard = board.Occupancy[Constants.WhiteRook];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.WhiteRook, bitboard.PopLSB());
        }

        bitboard = board.Occupancy[Constants.WhiteQueen];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.WhiteQueen, bitboard.PopLSB());
        }

        // Accumulate layer weights
        board.Apply(whiteAcc, blackAcc, Constants.BlackKing, board.BlackKingSquare);

        bitboard = board.Occupancy[Constants.BlackPawn];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.BlackPawn, bitboard.PopLSB());
        }

        bitboard = board.Occupancy[Constants.BlackKnight];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.BlackKnight, bitboard.PopLSB());
        }

        bitboard = board.Occupancy[Constants.BlackBishop];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.BlackBishop, bitboard.PopLSB());
        }

        bitboard = board.Occupancy[Constants.BlackRook];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.BlackRook, bitboard.PopLSB());
        }

        bitboard = board.Occupancy[Constants.BlackQueen];
        while (bitboard != 0)
        {
            board.Apply(whiteAcc, blackAcc, Constants.BlackQueen, bitboard.PopLSB());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    private static int ForwardCReLU(VectorShort* usAcc, VectorShort* themAcc, int bucket)
    {
        var sum = VectorInt.Zero;
        var featureWeightsPtr = NnueWeights.OutputWeights + bucket * AccumulatorSize * 2;
        var themWeightsPtr = featureWeightsPtr + AccumulatorSize;
        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            sum += AvxIntrinsics.Add(AvxIntrinsics.MultiplyAddAdjacent(
                    AvxIntrinsics.Max(AvxIntrinsics.Min(usAcc[i], Ceil), Floor),
                    featureWeightsPtr[i]),
                AvxIntrinsics.MultiplyAddAdjacent(
                    AvxIntrinsics.Max(AvxIntrinsics.Min(themAcc[i], Ceil), Floor),
                    themWeightsPtr[i]));
        }

        return VectorType.Sum(sum);
    }

    public static void RefreshWhite(this ref BoardStateData board, VectorShort* whiteAcc)
    {
        board.WhiteNeedsRefresh = false;
        board.WhiteMirrored = board.WhiteKingSquare.IsMirroredSide();
        board.WhiteInputBucket = NnueEvaluator.GetKingBucket(board.WhiteKingSquare);

        for (var i = 0; i < AccumulatorSize; i++)
        {
            whiteAcc[i] = NnueWeights.FeatureBiases[i];
        }

        // Accumulate layer weights
        AddWeights(whiteAcc, board.WhiteInputBucket, WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteKing, board.WhiteKingSquare));

        var bitboards = board.Occupancy[Constants.WhitePawn];

        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhitePawn, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteKnight, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteBishop, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteRook, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteQueen, bitboards.PopLSB()));
        }

        AddWeights(whiteAcc, board.WhiteInputBucket, WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackKing, board.BlackKingSquare));

        bitboards = board.Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackPawn, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackKnight, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackBishop, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackRook, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc, board.WhiteInputBucket,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackQueen, bitboards.PopLSB()));
        }
    }

    public static void RefreshBlack(this ref BoardStateData board, VectorShort* blackAcc)
    {
        board.BlackNeedsRefresh = false;
        board.BlackMirrored = board.BlackKingSquare.IsMirroredSide();
        board.BlackInputBucket = NnueEvaluator.GetKingBucket((byte)(board.BlackKingSquare ^ 0x38));


        for (var i = 0; i < AccumulatorSize; i++)
        {
            blackAcc[i] = NnueWeights.FeatureBiases[i];
        }

        // Accumulate layer weights
        AddWeights(blackAcc, board.BlackInputBucket, BlackFeatureIndices(board.BlackMirrored, Constants.WhiteKing, board.WhiteKingSquare));

        var bitboards = board.Occupancy[Constants.WhitePawn];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhitePawn, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhiteKnight, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhiteBishop, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhiteRook, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhiteQueen, bitboards.PopLSB()));
        }

        AddWeights(blackAcc, board.BlackInputBucket, BlackFeatureIndices(board.BlackMirrored, Constants.BlackKing, board.BlackKingSquare));

        bitboards = board.Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackPawn, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackKnight, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackBishop, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackRook, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            AddWeights(blackAcc, board.BlackInputBucket,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackQueen, bitboards.PopLSB()));
        }
    }
}