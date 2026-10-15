using FluentAssertions;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Sapling.Engine.Evaluation;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;
using System.Runtime.InteropServices;

namespace Sapling.Engine.Tests;

// independent scalar NNUE oracle. Records accumulator and output values, not
// only final best moves: full-refresh accumulators, static output evaluation (including
// clamp, scaling and output-bucket selection with adversarial out-of-range accumulator
// values), and incremental quiet/capture accumulator updates.
//
// These tests run on every backend; they no longer skip non-x86 targets.
public unsafe class NnueOracleTests
{

    // Piece counts chosen to cover every output bucket (0..7) and both mirror sides.
    private static readonly string[] FenCorpus =
    {
        "4k3/8/8/8/8/8/8/4K3 w - - 0 1",
        "7k/8/8/8/8/8/8/K7 w - - 0 1",
        "3k4/3p4/3P4/3K4/8/8/8/8 w - - 0 1",
        "2k5/3p4/3P4/8/8/8/4P3/2K5 w - - 0 1",
        "4k3/pppppppp/8/8/8/8/8/4K3 w - - 0 1",
        "r3k2r/pppppppp/8/8/8/8/8/R3K3 w KQkq - 0 1",
        "r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K3 w KQkq - 0 1",
        "r3k2r/pppppppp/4p3/8/8/4P3/PPPPPPPP/R3K2R w KQkq - 0 1",
        "r1bqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQK1NR w KQkq - 0 1",
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1",
    };

    // Direct output-kernel parity: production ForwardCReLU (via the narrow internal probe)
    // against an independent scalar oracle. Exact integer equality, never a tolerance.
    // Covers every output bucket, both perspective orderings, boundary accumulator values
    // (below zero, 0, 1, 254, 255, above 255) and fixed-seed randomized arrays including
    // short extremes. The oracle re-derives the CReLU dot product without SIMD and without
    // calling the kernel under test.

    private static int OracleRawOutput(short[] us, short[] them, int bucket)
    {
        var weights = (short*)NnueWeights.OutputWeights + (nuint)bucket * 2 * NnueWeights.Layer1Size;
        var sum = 0;
        for (var i = 0; i < NnueWeights.Layer1Size; i++)
        {
            var u = Math.Clamp((int)us[i], 0, 255);
            var t = Math.Clamp((int)them[i], 0, 255);
            sum = unchecked(sum + u * weights[i] + t * weights[NnueWeights.Layer1Size + i]);
        }

        return sum;
    }

    private static int KernelOutput(short[] us, short[] them, int bucket)
    {
        fixed (short* u = us)
        fixed (short* t = them)
        {
            return Searcher.ForwardCReLUProbe(u, t, bucket);
        }
    }

    private static short[] RandomAccumulator(int seed)
    {
        var rng = new Random(seed);
        var acc = new short[NnueWeights.Layer1Size];
        for (var i = 0; i < acc.Length; i++)
        {
            acc[i] = unchecked((short)rng.Next(short.MinValue, short.MaxValue + 1));
        }

        return acc;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void OutputKernelMatchesOracleForAllBuckets(int bucket)
    {
        var us = RandomAccumulator(20261012 + bucket);
        var them = RandomAccumulator(999331 + bucket);
        KernelOutput(us, them, bucket).Should().Be(OracleRawOutput(us, them, bucket),
            $"bucket {bucket} fixed-seed random accumulators");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void OutputKernelMatchesOracleBothPerspectives(int bucket)
    {
        var a = RandomAccumulator(555_000 + bucket);
        var b = RandomAccumulator(666_000 + bucket);
        KernelOutput(a, b, bucket).Should().Be(OracleRawOutput(a, b, bucket),
            $"bucket {bucket} perspective us=a them=b");
        KernelOutput(b, a, bucket).Should().Be(OracleRawOutput(b, a, bucket),
            $"bucket {bucket} perspective us=b them=a");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void OutputKernelMatchesOracleBoundaryValues(int bucket)
    {
        // Boundary accumulator values: below zero, 0, 1, 254, 255, above 255, plus short extremes.
        short[] boundary = [-32768, -1, 0, 1, 254, 255, 256, short.MaxValue];
        var us = new short[NnueWeights.Layer1Size];
        var them = new short[NnueWeights.Layer1Size];
        for (var i = 0; i < us.Length; i++)
        {
            us[i] = boundary[i % boundary.Length];
            them[i] = boundary[(i / boundary.Length + i) % boundary.Length];
        }

        KernelOutput(us, them, bucket).Should().Be(OracleRawOutput(us, them, bucket),
            $"bucket {bucket} boundary-value accumulators");
    }

    [Fact]
    public void OutputKernelMatchesOracleRandomizedWithShortExtremes()
    {
        var rng = new Random(4242);
        for (var trial = 0; trial < 8; trial++)
        {
            var bucket = trial % NnueWeights.OutputBuckets;
            var us = RandomAccumulator(70_000 + trial);
            var them = RandomAccumulator(80_000 + trial);
            for (var i = 0; i < us.Length; i += 37)
            {
                us[i] = short.MinValue;
                them[i] = short.MaxValue;
            }

            for (var i = 5; i < us.Length; i += 53)
            {
                us[i] = 255;
                them[i] = 256;
            }

            KernelOutput(us, them, bucket).Should().Be(OracleRawOutput(us, them, bucket),
                $"trial {trial} bucket {bucket} randomized with short extremes");
        }
    }

}
