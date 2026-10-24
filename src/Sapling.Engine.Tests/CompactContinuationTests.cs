using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
namespace Sapling.Engine.Tests;
public class CompactContinuationTests
{
    [Fact]
    public void EveryValidPieceAndDestinationHasOneCompactIndex()
    {
        var seen = new bool[Searcher.ContinuationHistoryLength];
        for (int pp = 1; pp <= 12; pp++)
        for (int ps = 0; ps < 64; ps++)
        for (int mp = 1; mp <= 12; mp++)
        for (int ms = 0; ms < 64; ms++)
        {
            uint previous = MoveExtensions.EncodeNormalMove(pp, (ps + 1) % 64, ps);
            uint move = MoveExtensions.EncodeNormalMove(mp, (ms + 1) % 64, ms);
            var index = Searcher.ContinuationIndex(previous, move);
            Assert.InRange(index, 0, seen.Length - 1);
            Assert.False(seen[index]);
            seen[index] = true;
        }
        Assert.All(seen, value => Assert.True(value));
        Assert.Equal(589824, seen.Length);
    }
}
