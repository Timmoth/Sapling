using FluentAssertions;

namespace iBlunder.Engine.Tests;

public class InsufficientMaterialTests
{
    [Theory]
    [InlineData("7K/8/8/8/8/2p5/3P4/7k b - - 0 1", false)]
    [InlineData("7K/8/8/8/8/8/8/7k w - - 0 1", true)]
    [InlineData("7K/8/5n2/6N1/8/8/8/7k w - - 0 1", true)]
    [InlineData("7K/8/5n2/6N1/4B3/8/8/7k w - - 0 1", false)]
    [InlineData("7K/8/6B1/8/8/8/8/7k w - - 0 1", true)]
    [InlineData("7K/4b3/6B1/8/8/8/8/7k w - - 0 1", true)]
    [InlineData("7K/8/4N3/5N2/8/8/8/7k w - - 0 1", true)]
    [InlineData("8/8/8/7K/8/3k4/8/8 w - - 0 157", true)]
    [InlineData("5B2/8/8/8/2K5/8/8/2k5 b - - 0 176", true)]
    [InlineData("8/b7/8/8/7K/8/2k5/8 w - - 0 57", true)]
    [InlineData("8/8/8/8/8/6k1/NK6/8 b - - 0 96", true)]
    public void ApplyUnapplyMatchesInitialState(string fen, bool isInsufficientMaterial)
    {
        BoardStateExtensions.CreateBoardFromFen(fen).InsufficientMatingMaterial().Should().Be(isInsufficientMaterial);
    }
}