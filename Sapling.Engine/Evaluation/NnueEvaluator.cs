using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Sapling.Engine.Evaluation;

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
        if (board.WhiteMirrored != board.ShouldWhiteMirrored)
        {
            board.MirrorWhite(whiteAcc);
        }

        if (board.BlackMirrored != board.ShouldBlackMirrored)
        {
            board.MirrorBlack(blackAcc);
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

    public static void Deactivate(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc, int piece, int square)
    {
        var (bIdx, wIdx) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, piece, square);
        SubtractWeights(whiteAcc, wIdx);
        SubtractWeights(blackAcc, bIdx);
    }

    public static void Apply(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc, int piece, int square)
    {
        var (bIdx, wIdx) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, piece, square);
        AddWeights(whiteAcc, wIdx);
        AddWeights(blackAcc, bIdx);
    }

    public static void Replace(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc, int piece, int from, int to)
    {
        var (from_bIdx, from_wIdx) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, piece, from);
        var (to_bIdx, to_wIdx) = FeatureIndices(board.WhiteMirrored, board.BlackMirrored, piece, to);

        ReplaceWeights(whiteAcc, to_wIdx, from_wIdx);
        ReplaceWeights(blackAcc, to_bIdx, from_bIdx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReplaceWeights(VectorShort* accuPtr, int addFeatureIndex, int removeFeatureIndex)
    {
        var addFeatureOffsetPtr = NnueWeights.FeatureWeights + addFeatureIndex * AccumulatorSize;
        var removeFeatureOffsetPtr = NnueWeights.FeatureWeights + removeFeatureIndex * AccumulatorSize;
        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            accuPtr[i] += addFeatureOffsetPtr[i] - removeFeatureOffsetPtr[i];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SubtractWeights(VectorShort* accuPtr, int inputFeatureIndex)
    {
        var featurePtr = NnueWeights.FeatureWeights + inputFeatureIndex * AccumulatorSize;
        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            accuPtr[i] -= featurePtr[i];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddWeights(VectorShort* accuPtr, int inputFeatureIndex)
    {
        var featurePtr = NnueWeights.FeatureWeights + inputFeatureIndex * AccumulatorSize;
        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            accuPtr[i] += featurePtr[i];
        }
    }

    public static void FillAccumulators(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc)
    {
        board.WhiteMirrored = board.ShouldWhiteMirrored = board.WhiteKingSquare.IsMirroredSide();
        board.BlackMirrored = board.ShouldBlackMirrored = board.BlackKingSquare.IsMirroredSide();
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

    public static void MirrorWhite(this ref BoardStateData board, VectorShort* whiteAcc)
    {
        board.WhiteMirrored = board.ShouldWhiteMirrored;
        for (var i = 0; i < AccumulatorSize; i++)
        {
            whiteAcc[i] = NnueWeights.FeatureBiases[i];
        }

        // Accumulate layer weights
        AddWeights(whiteAcc, WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteKing, board.WhiteKingSquare));

        var bitboards = board.Occupancy[Constants.WhitePawn];

        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhitePawn, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteKnight, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteBishop, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteRook, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.WhiteQueen, bitboards.PopLSB()));
        }

        AddWeights(whiteAcc, WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackKing, board.BlackKingSquare));

        bitboards = board.Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackPawn, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackKnight, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackBishop, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackRook, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            AddWeights(whiteAcc,
                WhiteFeatureIndices(board.WhiteMirrored, Constants.BlackQueen, bitboards.PopLSB()));
        }
    }

    public static void MirrorBlack(this ref BoardStateData board, VectorShort* blackAcc)
    {
        board.BlackMirrored = board.ShouldBlackMirrored;
        for (var i = 0; i < AccumulatorSize; i++)
        {
            blackAcc[i] = NnueWeights.FeatureBiases[i];
        }

        // Accumulate layer weights
        AddWeights(blackAcc, BlackFeatureIndices(board.BlackMirrored, Constants.WhiteKing, board.WhiteKingSquare));

        var bitboards = board.Occupancy[Constants.WhitePawn];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhitePawn, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteKnight];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhiteKnight, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteBishop];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhiteBishop, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteRook];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhiteRook, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.WhiteQueen];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.WhiteQueen, bitboards.PopLSB()));
        }

        AddWeights(blackAcc, BlackFeatureIndices(board.BlackMirrored, Constants.BlackKing, board.BlackKingSquare));

        bitboards = board.Occupancy[Constants.BlackPawn];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackPawn, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackKnight];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackKnight, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackBishop];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackBishop, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackRook];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackRook, bitboards.PopLSB()));
        }

        bitboards = board.Occupancy[Constants.BlackQueen];
        while (bitboards != 0)
        {
            AddWeights(blackAcc,
                BlackFeatureIndices(board.BlackMirrored, Constants.BlackQueen, bitboards.PopLSB()));
        }
    }
}