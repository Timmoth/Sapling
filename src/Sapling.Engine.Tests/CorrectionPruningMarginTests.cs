using FluentAssertions;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

public class CorrectionPruningMarginTests
{
    [Theory]
    [InlineData(4, 67, 100, 100, 0.5f, 268)]
    [InlineData(4, 67, 100, 200, 0.5f, 318)]
    [InlineData(4, 67, 200, 100, 0.5f, 318)]
    [InlineData(4, 67, 100, 200, 0.0f, 268)]
    public void CorrectionMagnitudeWidensReverseFutilityMargin(int depth, int baseMargin, int rawEval,
        int correctedEval, float scale, int expected)
    {
        PruningMargins.ReverseFutility(depth, baseMargin, rawEval, correctedEval, scale)
            .Should().Be(expected);
    }
}
