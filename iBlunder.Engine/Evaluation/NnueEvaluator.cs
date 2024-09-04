using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace iBlunder.Engine.Evaluation;

public class NnueEvaluator
{
    private const int Scale = 400;
    private const int Q = 255 * 64;

    private const int ColorStride = 64 * 6;
    private const int PieceStride = 64;

    private static readonly Vector256<short> Ceil = Vector256.Create<short>(255);
    private static readonly Vector256<short> Floor = Vector256.Create<short>(0);
    public readonly short[] BlackAccumulator = new short[NnueWeights.Layer1Size];
    public readonly short[] WhiteAccumulator = new short[NnueWeights.Layer1Size];

    public NnueEvaluator()
    {
        Array.Copy(NnueWeights.FeatureBiases, WhiteAccumulator, WhiteAccumulator.Length);
        Array.Copy(NnueWeights.FeatureBiases, BlackAccumulator, BlackAccumulator.Length);
    }

    public bool IsSame(NnueEvaluator other)
    {
        return WhiteAccumulator.SequenceEqual(other.WhiteAccumulator) &&
               BlackAccumulator.SequenceEqual(other.BlackAccumulator);
    }

    public void ResetTo(NnueEvaluator other)
    {
        Array.Copy(other.WhiteAccumulator, WhiteAccumulator, WhiteAccumulator.Length);
        Array.Copy(other.BlackAccumulator, BlackAccumulator, BlackAccumulator.Length);
    }

    public static NnueEvaluator Clone(NnueEvaluator other)
    {
        var net = new NnueEvaluator();
        Array.Copy(other.WhiteAccumulator, net.WhiteAccumulator, net.WhiteAccumulator.Length);
        Array.Copy(other.BlackAccumulator, net.BlackAccumulator, net.BlackAccumulator.Length);
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
    private static unsafe void Add(short* accuPtr, short* featurePtr, int i)
    {
        Avx.Store(accuPtr + i, Avx2.Add(Avx.LoadVector256(accuPtr + i), Avx.LoadVector256(featurePtr + i)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Remove(short* accuPtr, short* featurePtr, int i)
    {
        Avx.Store(accuPtr + i, Avx2.Subtract(Avx.LoadVector256(accuPtr + i), Avx.LoadVector256(featurePtr + i)));
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
    private static unsafe void Replace(short* accuPtr, short* addFeatureOffsetPtr, short* removeFeatureOffsetPtr, int i)
    {
        Avx.Store(accuPtr + i,
            Avx2.Add(
                Avx.LoadVector256(accuPtr + i),
                Avx2.Subtract(
                    Avx.LoadVector256(addFeatureOffsetPtr + i),
                    Avx.LoadVector256(removeFeatureOffsetPtr + i))));
    }

    private static void ReplaceWeights(short[] accu, int addFeatureIndex, int removeFeatureIndex)
    {
        unsafe
        {
            fixed (short* accuPtr = accu)
            fixed (short* featureWeightsPtr = NnueWeights.FeatureWeights)
            {
                var addFeatureOffsetPtr = featureWeightsPtr + addFeatureIndex * NnueWeights.Layer1Size;
                var removeFeatureOffsetPtr = featureWeightsPtr + removeFeatureIndex * NnueWeights.Layer1Size;

                // Process in chunks
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
            }
        }
    }

    private static void SubtractWeights(short[] accu, int inputFeatureIndex)
    {
        unsafe
        {
            fixed (short* accuPtr = accu)
            fixed (short* featureWeightsPtr = NnueWeights.FeatureWeights)
            {
                var featurePtr = featureWeightsPtr + inputFeatureIndex * NnueWeights.Layer1Size;
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
            }
        }
    }

    private static void AddWeights(short[] accumulator, int inputFeatureIndex)
    {
        unsafe
        {
            fixed (short* accuPtr = accumulator)
            fixed (short* featureWeightsPtr = NnueWeights.FeatureWeights)
            {
                var featurePtr = featureWeightsPtr + inputFeatureIndex * NnueWeights.Layer1Size;
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
            }
        }
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
    private static unsafe void CRelU(Vector256<int>* accumulator, short* accuPtr, short* featurePtr, int i)
    {
        *accumulator += Avx2.MultiplyAddAdjacent(
            Vector256.Max(Vector256.Min(Avx.LoadVector256(accuPtr + i), Ceil), Floor),
            Avx.LoadVector256(featurePtr + i));
    }

    private static int ForwardCReLU(short[] us, short[] them)
    {
        var sum = Vector256<int>.Zero;
        unsafe
        {
            fixed (short* usAcc = us)
            fixed (short* themAcc = them)
            fixed (short* featureWeightsPtr = NnueWeights.OutputWeights)
            {
                var sumAddr = &sum;
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
                CRelU(sumAddr, themAcc, themWeightsPtr, 80);
                CRelU(sumAddr, themAcc, themWeightsPtr, 64);
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
            }
        }

        return Vector256.Sum(sum);
    }
}