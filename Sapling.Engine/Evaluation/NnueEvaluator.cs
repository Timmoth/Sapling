using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

public unsafe class NnueEvaluator
{
#if AVX512
            const int VectorSize = 32; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#else
    const int VectorSize = 16; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#endif

    private const int Scale = 400;
    private const int Q = 255 * 64;

    private const int ColorStride = 64 * 6;
    private const int PieceStride = 64;

    private static readonly VectorShort Ceil = VectorType.Create<short>(255);
    private static readonly VectorShort Floor = VectorType.Create<short>(0);

    public VectorShort* BlackAccumulator;
    public VectorShort* WhiteAccumulator;
    public VectorShort* TempAccumulator;

    public bool ShouldWhiteMirrored = false;
    public bool ShouldBlackMirrored = false;
    public bool WhiteMirrored = false;
    public bool BlackMirrored = false;
    public const int AccumulatorSize = NnueWeights.Layer1Size / VectorSize;
    public NnueEvaluator()
    {
        WhiteAccumulator = AllocateAccumulator();
        BlackAccumulator = AllocateAccumulator();
        TempAccumulator = AllocateAccumulator();
    }
    public const int L1ByteSize = sizeof(short) * NnueWeights.Layer1Size;

    public static VectorShort* AllocateAccumulator()
    {
        const nuint alignment = 64;
        
        var block = NativeMemory.AlignedAlloc((nuint)L1ByteSize, alignment);
        NativeMemory.Clear(block, (nuint)L1ByteSize);

        return (VectorShort*)block;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SimdCopy(VectorShort* destination, VectorShort* source)
    {
        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            destination[i] = source[i];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearAccumulators()
    {
        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            WhiteAccumulator[i] = BlackAccumulator[i] = NnueWeights.FeatureBiases[i];
        }
    }

    ~NnueEvaluator()
    {
        NativeMemory.AlignedFree(WhiteAccumulator);
        NativeMemory.AlignedFree(BlackAccumulator);
    }

    public void ResetTo(NnueEvaluator other)
    {
        SimdCopy(WhiteAccumulator, other.WhiteAccumulator);
        SimdCopy(BlackAccumulator, other.BlackAccumulator);
    }

    public static NnueEvaluator Clone(NnueEvaluator other)
    {
        var net = new NnueEvaluator
        {
            WhiteMirrored = other.WhiteMirrored,
            BlackMirrored = other.BlackMirrored,
            ShouldWhiteMirrored = other.ShouldWhiteMirrored,
            ShouldBlackMirrored = other.ShouldBlackMirrored
        };
        SimdCopy(net.WhiteAccumulator, other.WhiteAccumulator);
        SimdCopy(net.BlackAccumulator, other.BlackAccumulator);
        return net;
    }

    const int BucketDivisor = (32 + NnueWeights.OutputBuckets - 1) / NnueWeights.OutputBuckets;
    public int Evaluate(BoardState board)
    {
        if (WhiteMirrored != ShouldWhiteMirrored)
        {
            MirrorWhite(board);
        }
        if (BlackMirrored != ShouldBlackMirrored)
        {
            MirrorBlack(board);
        }

        var bucket = (board.PieceCount - 2) / BucketDivisor;

        var output = board.WhiteToMove
            ? ForwardCReLU(WhiteAccumulator, BlackAccumulator, bucket)
            : ForwardCReLU(BlackAccumulator, WhiteAccumulator, bucket);

        return (output + NnueWeights.OutputBiases[bucket]) * Scale / Q;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int blackIdx, int whiteIdx) FeatureIndices(int piece, int square)
    {
        var whitePieceSquare = square;
        var blackPieceSquare = square ^ 0x38;

        if (WhiteMirrored)
        {
            whitePieceSquare ^= 7;
        }

        if (BlackMirrored)
        {
            blackPieceSquare ^= 7;
        }

        var white = (piece + 1) % 2;
        var type = (piece >> 1) - white;

        return (white * ColorStride + type * PieceStride + (blackPieceSquare),
            (white ^ 1) * ColorStride + type * PieceStride + whitePieceSquare);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int WhiteFeatureIndices(int piece, byte square)
    {
        if (WhiteMirrored)
        {
            square ^= 7;
        }

        var white = (piece + 1) % 2;
        var type = (piece >> 1) - white;

        return (white ^ 1) * ColorStride + type * PieceStride + square;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int BlackFeatureIndices(int piece, byte square)
    {
        if (BlackMirrored)
        {
            square ^= 7;
        }

        var white = (piece + 1) % 2;
        var type = (piece >> 1) - white;

        return white * ColorStride + type * PieceStride + square ^ 0x38;
    }

    public void Deactivate(int piece, int square)
    {
        var (bIdx, wIdx) = FeatureIndices(piece, square);
        SubtractWeights(BlackAccumulator, bIdx);
        SubtractWeights(WhiteAccumulator, wIdx);
    }

    public void Apply(int piece, int square)
    {
        var (bIdx, wIdx) = FeatureIndices(piece, square);
        AddWeights(BlackAccumulator, bIdx);
        AddWeights(WhiteAccumulator, wIdx);
    }

    public void Replace(int piece, int from, int to)
    {
        var (from_bIdx, from_wIdx) = FeatureIndices(piece, from);
        var (to_bIdx, to_wIdx) = FeatureIndices(piece, to);

        ReplaceWeights(BlackAccumulator, to_bIdx, from_bIdx);
        ReplaceWeights(WhiteAccumulator, to_wIdx, from_wIdx);
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

    public void FillAccumulators(BoardState board)
    {
        ClearAccumulators();

        // Accumulate layer weights
        Apply(Constants.WhiteKing, board.WhiteKingSquare);

        var number = board.WhitePawns;
        while (number != 0)
        {
            Apply(Constants.WhitePawn, number.PopLSB());
        }

        number = board.WhiteKnights;
        while (number != 0)
        {
            Apply(Constants.WhiteKnight, number.PopLSB());
        }

        number = board.WhiteBishops;
        while (number != 0)
        {
            Apply(Constants.WhiteBishop, number.PopLSB());
        }

        number = board.WhiteRooks;
        while (number != 0)
        {
            Apply(Constants.WhiteRook, number.PopLSB());
        }

        number = board.WhiteQueens;
        while (number != 0)
        {
            Apply(Constants.WhiteQueen, number.PopLSB());
        }

        // Accumulate layer weights
        Apply(Constants.BlackKing, board.BlackKingSquare);

        number = board.BlackPawns;
        while (number != 0)
        {
            Apply(Constants.BlackPawn, number.PopLSB());
        }

        number = board.BlackKnights;
        while (number != 0)
        {
            Apply(Constants.BlackKnight, number.PopLSB());
        }

        number = board.BlackBishops;
        while (number != 0)
        {
            Apply(Constants.BlackBishop, number.PopLSB());
        }

        number = board.BlackRooks;
        while (number != 0)
        {
            Apply(Constants.BlackRook, number.PopLSB());
        }

        number = board.BlackQueens;
        while (number != 0)
        {
            Apply(Constants.BlackQueen, number.PopLSB());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ForwardCReLU(VectorShort* usAcc, VectorShort* themAcc, int bucket)
    {
        var sum = VectorInt.Zero;
        var featureWeightsPtr = NnueWeights.OutputWeights + (bucket * AccumulatorSize * 2);
        var themWeightsPtr = featureWeightsPtr + AccumulatorSize;
        for (var i = AccumulatorSize - 1; i >= 0; i--)
        {
            sum += AvxIntrinsics.MultiplyAddAdjacent(
                       AvxIntrinsics.Max(AvxIntrinsics.Min(usAcc[i], Ceil), Floor),
                       featureWeightsPtr[i]) +
                   AvxIntrinsics.MultiplyAddAdjacent(
                       AvxIntrinsics.Max(AvxIntrinsics.Min(themAcc[i], Ceil), Floor),
                       themWeightsPtr[i]);
        }

        return VectorType.Sum(sum);
    }

    public void MirrorWhite(BoardState board)
    {
        WhiteMirrored = !WhiteMirrored;
        for (var i = 0; i < AccumulatorSize; i++)
        {
            TempAccumulator[i] = NnueWeights.FeatureBiases[i];
        }

        // Accumulate layer weights
        AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.WhiteKing, board.WhiteKingSquare));

        var number = board.WhitePawns;

        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.WhitePawn, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.WhiteKnights;
        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.WhiteKnight, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.WhiteBishops;
        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.WhiteBishop, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.WhiteRooks;
        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.WhiteRook, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.WhiteQueens;
        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.WhiteQueen, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.BlackKing, board.BlackKingSquare));

        number = board.BlackPawns;
        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.BlackPawn, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.BlackKnights;
        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.BlackKnight, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.BlackBishops;
        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.BlackBishop, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.BlackRooks;
        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.BlackRook, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.BlackQueens;
        while (number != 0)
        {
            AddWeights(TempAccumulator, WhiteFeatureIndices(Constants.BlackQueen, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        var ptr = WhiteAccumulator;
        WhiteAccumulator = TempAccumulator;
        TempAccumulator = ptr;
    }

    public void MirrorBlack(BoardState board)
    {
        BlackMirrored = !BlackMirrored;
        for (var i = 0; i < AccumulatorSize; i++)
        {
            TempAccumulator[i] = NnueWeights.FeatureBiases[i];
        }

        // Accumulate layer weights
        AddWeights(TempAccumulator, BlackFeatureIndices(Constants.WhiteKing, board.WhiteKingSquare));

        var number = board.WhitePawns;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.WhitePawn, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.WhiteKnights;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.WhiteKnight, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.WhiteBishops;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.WhiteBishop, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.WhiteRooks;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.WhiteRook, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.WhiteQueens;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.WhiteQueen, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        AddWeights(TempAccumulator, BlackFeatureIndices(Constants.BlackKing, board.BlackKingSquare));

        number = board.BlackPawns;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.BlackPawn, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.BlackKnights;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.BlackKnight, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.BlackBishops;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.BlackBishop, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.BlackRooks;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.BlackRook, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        number = board.BlackQueens;
        while (number != 0)
        {
            AddWeights(TempAccumulator, BlackFeatureIndices(Constants.BlackQueen, (byte)Bmi1.X64.TrailingZeroCount(number)));
            number &= number - 1;
        }

        var ptr = BlackAccumulator;
        BlackAccumulator = TempAccumulator;
        TempAccumulator = ptr;
    }
}