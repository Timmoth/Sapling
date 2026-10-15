using Sapling.Engine.Search;
using Xunit;

namespace Sapling.Engine.Tests;

public class PerftBoundaryTests
{
    [Fact]
    public void DepthOne_CountsRootMovesOnly()
    {
        var board = Constants.InitialBoard;
        Assert.Equal(20UL, board.PerftRootSequential(1, false).Aggregate(0UL, (sum, row) => sum + row.nodes));
        Assert.Equal(20UL, board.PerftRootParallel(1, false).Aggregate(0UL, (sum, row) => sum + row.nodes));
    }

    [Fact]
    public void RootDivide_RejectsNonPositiveDepth()
    {
        var board = Constants.InitialBoard;
        Assert.Throws<ArgumentOutOfRangeException>(() => board.PerftRootSequential(0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.PerftRootParallel(-1, false));
    }
}
