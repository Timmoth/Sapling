using System.Buffers.Binary;
using System.Text;
using Sapling.Engine;
using Sapling.Engine.MoveGen;

namespace Sapling.Utils
{
    public static class Constants
    {
        public const int Layer1Size = 1280;

        public static string UnrollAndInsert(this string source, string replace, Func<int, string> line, int vectorSize)
        {
            var simdCopySourceBuilder = new StringBuilder();
            for (var i = 0; i < Constants.Layer1Size / vectorSize; i++)
            {
                simdCopySourceBuilder.AppendLine(line(i));
            }
            return source.Replace(replace, simdCopySourceBuilder.ToString());
        }
    }
    internal class Program
    {
        static void Main(string[] args)
        {

            var source = @"
using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    private static readonly VectorShort Ceil = VectorType.Create<short>(255);
    private static readonly VectorShort Floor = VectorType.Create<short>(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SimdResetAccumulators256(VectorShort* whiteAcc, VectorShort* blackAcc)
    {
        @SimdResetAccumulators256@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SimdResetAccumulators512(VectorShort* whiteAcc, VectorShort* blackAcc)
    {
        @SimdResetAccumulators512@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sub256(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* sub)
    {
        @Sub256@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sub512(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* sub)
    {
        @Sub512@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add256(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* add)
    {
        @Add256@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add512(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* add)
    {
        @Add512@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddWeights256(VectorShort* accuPtr, VectorShort* featurePtr)
    {
        @AddWeights256@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddWeights512(VectorShort* accuPtr, VectorShort* featurePtr)
    {
        @AddWeights512@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ForwardCReLU256(VectorShort* usAcc, VectorShort* themAcc, int bucket)
    {
        var sum = VectorInt.Zero;
        var featureWeightsPtr = NnueWeights.OutputWeights + bucket * AccumulatorSize * 2;
        var themWeightsPtr = featureWeightsPtr + AccumulatorSize;

        @ForwardCReLU256@

        return VectorType.Sum(sum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ForwardCReLU512(VectorShort* usAcc, VectorShort* themAcc, int bucket)
    {
        var sum = VectorInt.Zero;
        var featureWeightsPtr = NnueWeights.OutputWeights + bucket * AccumulatorSize * 2;
        var themWeightsPtr = featureWeightsPtr + AccumulatorSize;

        @ForwardCReLU512@

        return VectorType.Sum(sum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubAdd256(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* sub1, VectorShort* add1)
    {
        @SubAdd256@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubAdd512(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* sub1, VectorShort* add1)
    {
        @SubAdd512@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubSubAdd256(VectorShort* source, VectorShort* dest, VectorShort* sub1, VectorShort* sub2, VectorShort* add1)
    {
        @SubSubAdd256@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubSubAdd512(VectorShort* source, VectorShort* dest, VectorShort* sub1, VectorShort* sub2, VectorShort* add1)
    {
        @SubSubAdd512@
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubSubAddAdd256(VectorShort* source, VectorShort* dest, VectorShort* sub1, VectorShort* sub2, VectorShort* add1, VectorShort* add2)
    {
        @SubSubAddAdd256@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubSubAddAdd512(VectorShort* source, VectorShort* dest, VectorShort* sub1, VectorShort* sub2, VectorShort* add1, VectorShort* add2)
    {
        @SubSubAddAdd512@
    }
}
";

            source = source.UnrollAndInsert("@SimdResetAccumulators256@", (i) => $"whiteAcc[{i}] = blackAcc[{i}] = NnueWeights.FeatureBiases[{i}];", 16);
            source = source.UnrollAndInsert("@Sub256@", (i) => $"*(dest + {i}) = *(source + {i}) - *(sub + {i});", 16);
            source = source.UnrollAndInsert("@Add256@", (i) => $"*(dest + {i}) = *(source + {i}) + *(add + {i});", 16);
            source = source.UnrollAndInsert("@AddWeights256@", (i) => $"*(accuPtr + {i}) += *(featurePtr + {i});", 16);
            source = source.UnrollAndInsert("@ForwardCReLU256@", (i) => $"        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + {i}), Ceil), Floor), *(featureWeightsPtr + {i})) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + {i}), Ceil), Floor), *(themWeightsPtr + {i}));", 16);
            source = source.UnrollAndInsert("@SubAdd256@", (i) => $" *(dest + {i}) = *(source + {i}) - *(sub1 + {i}) + *(add1 + {i});", 16);
            source = source.UnrollAndInsert("@SubSubAdd256@", (i) => $"*(dest + {i}) = *(source + {i}) - *(sub1 + {i}) + *(add1 + {i}) - *(sub2 + {i});", 16);
            source = source.UnrollAndInsert("@SubSubAddAdd256@", (i) => $"*(dest + { i}) = *(source + { i}) -*(sub1 + { i}) + *(add1 + { i}) - *(sub2 + { i}) + *(add2 + {i});", 16);

            source = source.UnrollAndInsert("@SimdResetAccumulators512@", (i) => $"whiteAcc[{i}] = blackAcc[{i}] = NnueWeights.FeatureBiases[{i}];", 32);
            source = source.UnrollAndInsert("@Sub512@", (i) => $"*(dest + {i}) = *(source + {i}) - *(sub + {i});", 32);
            source = source.UnrollAndInsert("@Add512@", (i) => $"*(dest + {i}) = *(source + {i}) + *(add + {i});", 32);
            source = source.UnrollAndInsert("@AddWeights512@", (i) => $"*(accuPtr + {i}) += *(featurePtr + {i});", 32); 
            source = source.UnrollAndInsert("@ForwardCReLU512@", (i) => $"        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + {i}), Ceil), Floor), *(featureWeightsPtr + {i})) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + {i}), Ceil), Floor), *(themWeightsPtr + {i}));", 32);
            source = source.UnrollAndInsert("@SubAdd512@", (i) => $" *(dest + {i}) = *(source + {i}) - *(sub1 + {i}) + *(add1 + {i});", 32);
            source = source.UnrollAndInsert("@SubSubAdd512@", (i) => $" *(dest + {i}) = *(source + {i}) - *(sub1 + {i}) + *(add1 + {i}) - *(sub2 + {i});", 32);
            source = source.UnrollAndInsert("@SubSubAddAdd512@", (i) => $"*(dest + {i}) = *(source + {i}) -*(sub1 + {i}) + *(add1 + {i}) - *(sub2 + {i}) + *(add2 + {i});", 32);

            File.WriteAllText("./unrolled.cs", source.ToString());
        }
    }
}
