﻿using System.Buffers.Binary;
using System.Text;
using Sapling.Engine;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;

namespace Sapling.Utils
{
    internal class Program
    {
        public static string UnrollAndInsert(string source, string replace, Func<int, string> line)
        {
            var simdCopySourceBuilder = new StringBuilder();

            var avx512Elements = NnueWeights.Layer1Size / 32;
            var avx256Elements = NnueWeights.Layer1Size / 16;
            var i = 0;
            for (; i < avx512Elements; i++)
            {
                simdCopySourceBuilder.AppendLine(line(i));
            }

            simdCopySourceBuilder.AppendLine("#if !AVX512");
            for (; i < avx256Elements; i++)
            {
                simdCopySourceBuilder.AppendLine(line(i));
            }
            simdCopySourceBuilder.AppendLine("#endif");

            return source.Replace(replace, simdCopySourceBuilder.ToString());
        }

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
    public static void SimdResetAccumulators(VectorShort* whiteAcc, VectorShort* blackAcc)
    {
        @SimdResetAccumulators@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SimdResetAccumulator(VectorShort* acc)
    {
        @SimdResetAccumulator@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SimdCopy(VectorShort* dest, VectorShort* src)
    {
        @SimdCopy@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sub(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* sub)
    {
        @Sub@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* add)
    {
        @Add@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddWeights(VectorShort* accuPtr, VectorShort* featurePtr)
    {
        @AddWeights@
    }
 
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ForwardCReLU(VectorShort* usAcc, VectorShort* themAcc, int bucket)
    {
        var sum = VectorInt.Zero;
        var featureWeightsPtr = NnueWeights.OutputWeights + bucket * AccumulatorSize * 2;
        var themWeightsPtr = featureWeightsPtr + AccumulatorSize;

        @ForwardCReLU@

        return VectorType.Sum(sum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubAdd(
        VectorShort* source,
        VectorShort* dest,
        VectorShort* sub1, VectorShort* add1)
    {
        @SubAdd@
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubSubAdd(VectorShort* source, VectorShort* dest, VectorShort* sub1, VectorShort* sub2, VectorShort* add1)
    {
        @SubSubAdd@
    }

   
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SubSubAddAdd(VectorShort* source, VectorShort* dest, VectorShort* sub1, VectorShort* sub2, VectorShort* add1, VectorShort* add2)
    {
        @SubSubAddAdd@
    }
}
";

            source = UnrollAndInsert(source, "@SimdResetAccumulator@", (i) => $"*(acc+{i}) = *(NnueWeights.FeatureBiases+{i});");
            source = UnrollAndInsert(source, "@SimdResetAccumulators@", (i) => $"*(whiteAcc+{i}) = *(blackAcc+{i}) = *(NnueWeights.FeatureBiases+{i});");
            source = UnrollAndInsert(source, "@SimdCopy@", (i) => $"*(dest+{i}) = *(src+{i});");
            source = UnrollAndInsert(source, "@Sub@", (i) => $"*(dest + {i}) = *(source + {i}) - *(sub + {i});");
            source = UnrollAndInsert(source, "@Add@", (i) => $"*(dest + {i}) = *(source + {i}) + *(add + {i});");
            source = UnrollAndInsert(source, "@AddWeights@", (i) => $"*(accuPtr + {i}) += *(featurePtr + {i});");
            source = UnrollAndInsert(source, "@ForwardCReLU@", (i) => $"        sum += AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(usAcc + {i}), Ceil), Floor), *(featureWeightsPtr + {i})) + AvxIntrinsics.MultiplyAddAdjacent(AvxIntrinsics.Max(AvxIntrinsics.Min(*(themAcc + {i}), Ceil), Floor), *(themWeightsPtr + {i}));");
            source = UnrollAndInsert(source, "@SubAdd@", (i) => $" *(dest + {i}) = *(source + {i}) - *(sub1 + {i}) + *(add1 + {i});");
            source = UnrollAndInsert(source, "@SubSubAdd@", (i) => $"*(dest + {i}) = *(source + {i}) - *(sub1 + {i}) + *(add1 + {i}) - *(sub2 + {i});");
            source = UnrollAndInsert(source, "@SubSubAddAdd@", (i) => $"*(dest + { i}) = *(source + { i}) -*(sub1 + { i}) + *(add1 + { i}) - *(sub2 + { i}) + *(add2 + {i});");


            File.WriteAllText("./unrolled.cs", source.ToString());
        }
    }
}
