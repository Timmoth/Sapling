using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Sapling.Engine;
using Sapling.Engine.Transpositions;
using Xunit;

namespace Sapling.Engine.Tests;

public class AllocationHelperTests
{
    [Fact]
    public unsafe void BenchAllocator_RejectsInvalidCountsBeforeMultiplyingBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DataGen.Bench.AllocateTranspositions(0));
        Assert.ThrowsAny<ArgumentException>(() => DataGen.Bench.AllocateTranspositions((nuint)(nuint.MaxValue / 2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public unsafe void Allocate_NonPositiveCount_Throws(long count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryHelpers.Allocate<int>(count));
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryHelpers.AllocateMultiThreaded<int>(count));
    }

    [Fact]
    public unsafe void Allocate_HugeCount_ThrowsInsteadOfOverflowing()
    {
        // count * sizeof(long) would wrap ulong unchecked; must be rejected.
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryHelpers.Allocate<long>(long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryHelpers.AllocateMultiThreaded<long>(long.MaxValue));
    }

    [Fact]
    public unsafe void Allocate_ReturnsAlignedZeroedBlock()
    {
        const int count = 1000;
        int* block = MemoryHelpers.Allocate<int>(count);
        try
        {
            Assert.Equal((nuint)0, ((nuint)block % 64));
            for (var i = 0; i < count; i++)
            {
                Assert.Equal(0, block[i]);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(block);
        }
    }

    [Fact]
    public unsafe void AllocateMultiThreaded_ReturnsAlignedZeroedBlock()
    {
        const int count = 100_000;
        int* block = MemoryHelpers.AllocateMultiThreaded<int>(count);
        try
        {
            Assert.Equal((nuint)0, ((nuint)block % 64));
            for (var i = 0; i < count; i++)
            {
                Assert.Equal(0, block[i]);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(block);
        }
    }

    [Fact]
    public void CalculateTranspositionTableSize_SmallSizes_AreSane()
    {
        foreach (var mb in new[] { 1L, 2, 3, 4, 8, 64, 256, 1024 })
        {
            var count = TranspositionTableExtensions.CalculateTranspositionTableSize(mb);
            Assert.True(count > 0);
            Assert.True(BitOperations.IsPow2((ulong)count), $"{mb} MB must support mask indexing");
            // Entry count must stay within a factor of two of the requested bytes / entry size.
            long estimated = mb * 1024 * 1024 / 20;
            Assert.True(count >= estimated / 2 && count <= estimated * 2, $"{mb} MB");
        }
    }

    [Fact]
    public void CalculateTranspositionTableSize_AbsurdSizes_Throw()
    {
        Assert.Throws<OverflowException>(() => TranspositionTableExtensions.CalculateTranspositionTableSize(long.MaxValue));
    }

    [Fact]
    public void CalculateSizeInMb_RoundTripsKnownSizes()
    {
        foreach (var mb in new[] { 64L, 256, 1024 })
        {
            var count = TranspositionTableExtensions.CalculateTranspositionTableSize(mb);
            var inverted = TranspositionTableExtensions.CalculateSizeInMb((uint)count);
            Assert.Equal(count, TranspositionTableExtensions.CalculateTranspositionTableSize(inverted));
        }
    }

    [Fact]
    public void CalculateSizeInMb_NonPowerOfTwo_Throws()
    {
        Assert.Throws<ArgumentException>(() => TranspositionTableExtensions.CalculateSizeInMb(1000));
    }
}
