using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Tests;

public class SearchSelectivityTests
{
    [Fact]
    public void ShallowerVerificationRequiresAStrongerBound()
    {
        var ordinary = Searcher.ProbCutVerificationWindow(6, 300, 300);
        var optimistic = Searcher.ProbCutVerificationWindow(6, 300, 1000);
        Assert.Equal((6, 300), ordinary);
        Assert.True(optimistic.Depth < ordinary.Depth);
        Assert.True(optimistic.Beta > ordinary.Beta);
    }

    [Fact]
    public void VerificationDoesNotReduceAMateScoreOrEnterMateBand()
    {
        Assert.Equal((6, 300), Searcher.ProbCutVerificationWindow(6, 300, 28900));
        var nearBand = Searcher.ProbCutVerificationWindow(6, 26900, 28000);
        Assert.True(nearBand.Beta < TranspositionTableExtensions.PositiveCheckmateDetectionLimit);
        Assert.Equal(0, Searcher.ProbCutVerificationWindow(0, 300, 2000).Depth);
        Assert.True(Searcher.ProbCutVerificationWindow(1, 300, 2000).Depth >= 1);
    }
}
