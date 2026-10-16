using FluentAssertions;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

public class NullMoveVerificationTests
{
    [Theory]
    [InlineData(7, 8, false)]
    [InlineData(8, 8, true)]
    [InlineData(12, 8, true)]
    public void VerificationStartsAtConfiguredDepth(int depth, int minimumDepth, bool expected)
    {
        NullMoveVerification.ShouldVerify(depth, minimumDepth).Should().Be(expected);
    }
}
