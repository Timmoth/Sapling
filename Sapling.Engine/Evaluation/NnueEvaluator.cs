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
    private const int Scale = 400;
    private const int Q = 255 * 64;

    private const int ColorStride = 64 * 6;
    private const int PieceStride = 64;

    private static readonly VectorShort Ceil = VectorType.Create<short>(255);
    private static readonly VectorShort Floor = VectorType.Create<short>(0);

    public short* BlackAccumulator;
    public short* WhiteAccumulator;

    public NnueEvaluator()
    {
        WhiteAccumulator = AllocateAccumulator();
        BlackAccumulator = AllocateAccumulator();
        SimdCopy(WhiteAccumulator, NnueWeights.FeatureBiases);
        SimdCopy(BlackAccumulator, NnueWeights.FeatureBiases);
    }

    public static short* AllocateAccumulator()
    {
        const nuint alignment = 64;
        const nuint bytes = sizeof(short) * NnueWeights.Layer1Size;

        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (short*)block;
    }

    private static void SimdCopy(short* destination, short* source)
    {
        #if AVX512
            const int VectorSize = 32; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
        #else
            const int VectorSize = 16; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
        #endif


        nuint i = 0;
        for (; i + VectorSize <= NnueWeights.Layer1Size; i += VectorSize)
        {
            AvxIntrinsics.StoreAligned(destination + i, VectorType.LoadAligned(source + i));
        }

        // Copy remaining elements that don't fit in the vector
        for (; i < NnueWeights.Layer1Size; i++)
        {
            destination[i] = source[i];
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
        var net = new NnueEvaluator();
        SimdCopy(net.WhiteAccumulator, other.WhiteAccumulator);
        SimdCopy(net.BlackAccumulator, other.BlackAccumulator);
        return net;
    }

    public int Evaluate(bool isWhite)
    {
        var output = isWhite
            ? ForwardCReLU(WhiteAccumulator, BlackAccumulator)
            : ForwardCReLU(BlackAccumulator, WhiteAccumulator);

        return (output + NnueWeights.OutputBias) * Scale / Q;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int blackIdx, int whiteIdx) FeatureIndices(int piece, int square)
    {
        var white = (piece + 1) % 2;
        var type = (piece >> 1) - white;

        return (white * ColorStride + type * PieceStride + (square ^ 0x38),
            (white ^ 1) * ColorStride + type * PieceStride + square);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Add(short* accuPtr, short* featurePtr, int i)
    {
        AvxIntrinsics.StoreAligned(accuPtr + i, AvxIntrinsics.Add(VectorType.LoadAligned(accuPtr + i), VectorType.LoadAligned(featurePtr + i)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Remove(short* accuPtr, short* featurePtr, int i)
    {
        AvxIntrinsics.StoreAligned(accuPtr + i, AvxIntrinsics.Subtract(VectorType.LoadAligned(accuPtr + i), VectorType.LoadAligned(featurePtr + i)));
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
    private static void Replace(short* accuPtr, short* addFeatureOffsetPtr, short* removeFeatureOffsetPtr, int i)
    {
        AvxIntrinsics.StoreAligned(accuPtr + i,
            AvxIntrinsics.Add(
                VectorType.LoadAligned(accuPtr + i),
                AvxIntrinsics.Subtract(
                    VectorType.LoadAligned(addFeatureOffsetPtr + i),
                    VectorType.LoadAligned(removeFeatureOffsetPtr + i))));
    }

    private static void ReplaceWeights(short* accuPtr, int addFeatureIndex, int removeFeatureIndex)
    {
        var addFeatureOffsetPtr = NnueWeights.FeatureWeights + addFeatureIndex * NnueWeights.Layer1Size;
        var removeFeatureOffsetPtr = NnueWeights.FeatureWeights + removeFeatureIndex * NnueWeights.Layer1Size;

        // Process in chunks
        #if AVX512
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 0);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 32);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 64);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 96);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 128);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 160);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 192);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 224);
        #else
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 0);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 16);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 32);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 48);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 64);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 80);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 96);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 112);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 128);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 144);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 160);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 176);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 192);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 208);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 224);
            Replace(accuPtr, addFeatureOffsetPtr, removeFeatureOffsetPtr, 240);
        #endif
    }

    private static void SubtractWeights(short* accuPtr, int inputFeatureIndex)
    {
        var featurePtr = NnueWeights.FeatureWeights + inputFeatureIndex * NnueWeights.Layer1Size;


        #if AVX512
            Remove(accuPtr, featurePtr, 0);
            Remove(accuPtr, featurePtr, 32);
            Remove(accuPtr, featurePtr, 64);
            Remove(accuPtr, featurePtr, 96);
            Remove(accuPtr, featurePtr, 128);
            Remove(accuPtr, featurePtr, 160);
            Remove(accuPtr, featurePtr, 192);
            Remove(accuPtr, featurePtr, 224);
        #else
            Remove(accuPtr, featurePtr, 0);
            Remove(accuPtr, featurePtr, 16);
            Remove(accuPtr, featurePtr, 32);
            Remove(accuPtr, featurePtr, 48);
            Remove(accuPtr, featurePtr, 64);
            Remove(accuPtr, featurePtr, 80);
            Remove(accuPtr, featurePtr, 96);
            Remove(accuPtr, featurePtr, 112);
            Remove(accuPtr, featurePtr, 128);
            Remove(accuPtr, featurePtr, 144);
            Remove(accuPtr, featurePtr, 160);
            Remove(accuPtr, featurePtr, 176);
            Remove(accuPtr, featurePtr, 192);
            Remove(accuPtr, featurePtr, 208);
            Remove(accuPtr, featurePtr, 224);
            Remove(accuPtr, featurePtr, 240);
        #endif
    }

    private static void AddWeights(short* accuPtr, int inputFeatureIndex)
    {
        var featurePtr = NnueWeights.FeatureWeights + inputFeatureIndex * NnueWeights.Layer1Size;


        #if AVX512
            Add(accuPtr, featurePtr, 0);
            Add(accuPtr, featurePtr, 32);
            Add(accuPtr, featurePtr, 64);
            Add(accuPtr, featurePtr, 96);
            Add(accuPtr, featurePtr, 128);
            Add(accuPtr, featurePtr, 160);
            Add(accuPtr, featurePtr, 192);
            Add(accuPtr, featurePtr, 224);
        #else
            Add(accuPtr, featurePtr, 0);
            Add(accuPtr, featurePtr, 16);
            Add(accuPtr, featurePtr, 32);
            Add(accuPtr, featurePtr, 48);
            Add(accuPtr, featurePtr, 64);
            Add(accuPtr, featurePtr, 80);
            Add(accuPtr, featurePtr, 96);
            Add(accuPtr, featurePtr, 112);
            Add(accuPtr, featurePtr, 128);
            Add(accuPtr, featurePtr, 144);
            Add(accuPtr, featurePtr, 160);
            Add(accuPtr, featurePtr, 176);
            Add(accuPtr, featurePtr, 192);
            Add(accuPtr, featurePtr, 208);
            Add(accuPtr, featurePtr, 224);
            Add(accuPtr, featurePtr, 240);
        #endif
    }

    public void FillAccumulator(BoardState board)
    {
        // Accumulate layer weights
        Apply(Constants.WhiteKing, board.WhiteKingSquare);
        Apply(Constants.BlackKing, board.BlackKingSquare);

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
    private static void CRelU(VectorInt* accumulator, short* accuPtr, short* featurePtr, int i)
    {
        *accumulator += AvxIntrinsics.MultiplyAddAdjacent(
            AvxIntrinsics.Max(AvxIntrinsics.Min(VectorType.LoadAligned(accuPtr + i), Ceil), Floor),
            VectorType.LoadAligned(featurePtr + i));
    }

    private static int ForwardCReLU(short* usAcc, short* themAcc)
    {
        var sum = VectorInt.Zero;
        var featureWeightsPtr = NnueWeights.OutputWeights;
        var sumAddr = &sum;

        #if AVX512
            CRelU(sumAddr, usAcc, featureWeightsPtr, 0);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 32);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 64);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 96);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 128);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 160);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 192);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 224);

            var themWeightsPtr = featureWeightsPtr + NnueWeights.Layer1Size;
            CRelU(sumAddr, themAcc, themWeightsPtr, 0);
            CRelU(sumAddr, themAcc, themWeightsPtr, 32);
            CRelU(sumAddr, themAcc, themWeightsPtr, 64);
            CRelU(sumAddr, themAcc, themWeightsPtr, 96);
            CRelU(sumAddr, themAcc, themWeightsPtr, 128);
            CRelU(sumAddr, themAcc, themWeightsPtr, 160);
            CRelU(sumAddr, themAcc, themWeightsPtr, 192);
            CRelU(sumAddr, themAcc, themWeightsPtr, 224);
        #else
            CRelU(sumAddr, usAcc, featureWeightsPtr, 0);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 16);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 32);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 48);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 64);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 80);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 96);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 112);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 128);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 144);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 160);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 176);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 192);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 208);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 224);
            CRelU(sumAddr, usAcc, featureWeightsPtr, 240);

            var themWeightsPtr = featureWeightsPtr + NnueWeights.Layer1Size;
            CRelU(sumAddr, themAcc, themWeightsPtr, 0);
            CRelU(sumAddr, themAcc, themWeightsPtr, 16);
            CRelU(sumAddr, themAcc, themWeightsPtr, 32);
            CRelU(sumAddr, themAcc, themWeightsPtr, 48);
            CRelU(sumAddr, themAcc, themWeightsPtr, 64);
            CRelU(sumAddr, themAcc, themWeightsPtr, 80);
            CRelU(sumAddr, themAcc, themWeightsPtr, 96);
            CRelU(sumAddr, themAcc, themWeightsPtr, 112);
            CRelU(sumAddr, themAcc, themWeightsPtr, 128);
            CRelU(sumAddr, themAcc, themWeightsPtr, 144);
            CRelU(sumAddr, themAcc, themWeightsPtr, 160);
            CRelU(sumAddr, themAcc, themWeightsPtr, 176);
            CRelU(sumAddr, themAcc, themWeightsPtr, 192);
            CRelU(sumAddr, themAcc, themWeightsPtr, 208);
            CRelU(sumAddr, themAcc, themWeightsPtr, 224);
            CRelU(sumAddr, themAcc, themWeightsPtr, 240);
        #endif

        return VectorType.Sum(sum);
    }
}