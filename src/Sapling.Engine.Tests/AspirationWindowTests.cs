using Sapling.Engine.Search;
using Xunit;

namespace Sapling.Engine.Tests;

public class AspirationWindowTests
{
    [Theory]
    [InlineData(0, 37)]
    [InlineData(1, 74)]
    [InlineData(2, 148)]
    [InlineData(3, 296)]
    [InlineData(4, 592)]
    [InlineData(5, 1184)]
    [InlineData(9, 18944)]
    public void Window_DoublesAfterEachFailedSearch(int retry, int expected)
    {
        Assert.Equal(expected, Searcher.GetAspirationWindow(retry));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(20)]
    public void Window_HandlesBounds(int retry)
    {
        var expected = retry < 0 ? 37 : Sapling.Engine.Constants.MaxScore;
        Assert.Equal(expected, Searcher.GetAspirationWindow(retry));
    }
}
