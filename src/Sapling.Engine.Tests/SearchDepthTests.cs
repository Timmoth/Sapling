using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

/// <summary>
/// 'go depth N' must actually complete iteration N. The iterative deepening loop used to
/// stop one ply short, so every depth-limited search (and the bench) under-searched.
/// </summary>
public class SearchDepthTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(9)]
    public void DepthBoundSearch_CompletesTheRequestedDepth(int depth)
    {
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);

        var result = searcher.DepthBoundSearch(state, depth);

        Assert.Equal(depth, result.depthSearched);
        Assert.NotEmpty(result.move);
    }

    [Fact]
    public void DepthBoundSearch_AtTheEngineDepthLimit_StaysInBounds()
    {
        // Dead material keeps the tree shallow so the deepest legal iteration is actually reachable.
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen("7k/8/8/8/8/8/8/K7 w - - 0 1"));
        using var searcher = new ParallelSearcher(1024);

        var result = searcher.DepthBoundSearch(state, Constants.MaxSearchDepth);

        Assert.Equal(Constants.MaxSearchDepth, result.depthSearched);
        Assert.True(result.move.Count <= Constants.MaxSearchDepth);
    }

    [Fact]
    public void Abort_RefusesSearchesStartedAfterIt()
    {
        using var state = GameState.InitialState();
        var searcher = new ParallelSearcher(1024);
        try
        {
            searcher.Abort();

            Assert.Throws<System.InvalidOperationException>(() => searcher.DepthBoundSearch(state, 5));
        }
        finally
        {
            searcher.Dispose();
        }
    }
}
