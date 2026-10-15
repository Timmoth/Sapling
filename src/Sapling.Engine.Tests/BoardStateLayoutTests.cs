using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace Sapling.Engine.Tests;

public class BoardStateLayoutTests
{
    // The size contract relied on by every CopyBlock/MemoryCopy site (BoardStateSize vs sizeof)
    // and by the explicit field offsets. If either drifts, board copies silently corrupt state.
    [Fact]
    public unsafe void BoardStateData_SizeContract_Holds()
    {
        sizeof(BoardStateData).Should().Be((int)BoardStateData.BoardStateSize);
        sizeof(BoardStateData).Should().Be(168);
        (168 % 8).Should().Be(0, "array elements must stay 8-byte aligned");
    }

    [Fact]
    public void BoardStateData_CriticalFieldOffsets_Hold()
    {
        Off("Occupancy").Should().Be(0);
        Off("Hash").Should().Be(120);
        Off("TurnCount").Should().Be(128);
        Off("PawnHash").Should().Be(138);
        Off("WhiteMaterialHash").Should().Be(146);
        Off("BlackMaterialHash").Should().Be(154);
        Off("Is960").Should().Be(166);
    }

    [Fact]
    public unsafe void AccumulatorState_SizeContract_Holds()
    {
        sizeof(AccumulatorState).Should().Be(56);
        OffAcc("Evaluation").Should().Be(44);
        OffAcc("Move").Should().Be(48);
    }

    private static int Off(string field) => (int)Marshal.OffsetOf(typeof(BoardStateData), field);
    private static int OffAcc(string field) => (int)Marshal.OffsetOf(typeof(AccumulatorState), field);
}
