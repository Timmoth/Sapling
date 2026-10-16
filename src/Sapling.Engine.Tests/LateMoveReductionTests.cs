using FluentAssertions;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

public class LateMoveReductionTests
{
    [Theory]
    [InlineData(false, 4, 0.5f, 4)]
    [InlineData(true, 4, 1.0f, 4)]
    [InlineData(true, 4, 0.75f, 3)]
    [InlineData(true, 0, 0.5f, 0)]
    [InlineData(true, 4, 0.5f, 2)]
    public void ExactTtEntryScalesLateMoveReduction(bool hasEntry, int reduction, float scale, int expected)
    {
        LateMoveReduction.AdjustForExactTt(reduction, hasEntry, scale, 4).Should().Be(expected);
    }
}
