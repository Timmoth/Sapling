using System.Runtime.InteropServices;
using Xunit;

namespace Sapling.Engine.Tests;

public class BoardStateLayoutTests
{
    // The size contract relied on by every CopyBlock/MemoryCopy site (BoardStateSize vs sizeof)
    // and by the explicit field offsets. If either drifts, board copies silently corrupt state.
    [Fact]
    public unsafe void BoardStateData_SizeContract_Holds()
    {
        Assert.Equal((int)BoardStateData.BoardStateSize, sizeof(BoardStateData));
        Assert.Equal(168, sizeof(BoardStateData));
        Assert.True((168 % 8) == 0, "array elements must stay 8-byte aligned");
    }

    [Fact]
    public void BoardStateData_CriticalFieldOffsets_Hold()
    {
        Assert.Equal(0, Off("Occupancy"));
        Assert.Equal(120, Off("Hash"));
        Assert.Equal(152, Off("TurnCount"));
        Assert.Equal(128, Off("PawnHash"));
        Assert.Equal(136, Off("WhiteMaterialHash"));
        Assert.Equal(144, Off("BlackMaterialHash"));
        Assert.Equal(166, Off("Is960"));
    }

    [Fact]
    public unsafe void BoardStateData_HashesRemainAlignedAcrossFrames()
    {
        var boards = stackalloc BoardStateData[121];
        for (var i = 0; i < 121; i++)
        {
            Assert.Equal(0UL, (ulong)(&boards[i].Hash) % 8);
            Assert.Equal(0UL, (ulong)(&boards[i].PawnHash) % 8);
            Assert.Equal(0UL, (ulong)(&boards[i].WhiteMaterialHash) % 8);
            Assert.Equal(0UL, (ulong)(&boards[i].BlackMaterialHash) % 8);
        }
    }

    [Fact]
    public unsafe void AccumulatorState_SizeContract_Holds()
    {
        Assert.Equal(56, sizeof(AccumulatorState));
        Assert.Equal(44, OffAcc("Evaluation"));
        Assert.Equal(48, OffAcc("Move"));
    }

    private static int Off(string field) => (int)Marshal.OffsetOf(typeof(BoardStateData), field);
    private static int OffAcc(string field) => (int)Marshal.OffsetOf(typeof(AccumulatorState), field);
}
