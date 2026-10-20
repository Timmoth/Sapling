using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

public unsafe class ContinuationHistoryTests
{
    [Fact]
    public void RepliesLearnOnlyInTheirContextsAndNullMovesBreakTheChain()
    {
        using var owner = new ParallelSearcher(1024);
        var worker = owner.Searchers[0];
        uint previous = MoveExtensions.EncodeNormalMove(Constants.BlackKnight, 57, 42);
        uint other = MoveExtensions.EncodeNormalMove(Constants.BlackKnight, 62, 45);
        uint earlier = MoveExtensions.EncodeNormalMove(Constants.WhitePawn, 12, 28);
        uint winner = MoveExtensions.EncodeNormalMove(Constants.WhiteKnight, 1, 18);
        uint loser = MoveExtensions.EncodeNormalMove(Constants.WhiteKnight, 6, 21);
        uint untouched = MoveExtensions.EncodeNormalMove(Constants.WhitePawn, 11, 27);
        uint* searched = stackalloc uint[] { loser };
        worker.UpdateContinuations(previous, earlier, winner, searched, 1, 5);
        Assert.True(worker.ContinuationScore(previous, earlier, winner) > 0);
        Assert.True(worker.ContinuationScore(previous, earlier, loser) < 0);
        Assert.Equal(0, worker.ContinuationScore(other, 0, winner));
        Assert.Equal(0, worker.ContinuationScore(previous, earlier, untouched));
        Assert.Equal(0, worker.ContinuationScore(0, earlier, winner));
        worker.UpdateContinuations(0, earlier, untouched, null, 0, 5);
        Assert.Equal(0, worker.ContinuationHistory[Searcher.ContinuationIndex(earlier, untouched)]);
        for (int i = 0; i < 20000; i++)
            worker.UpdateContinuations(previous, earlier, winner, searched, 1, 120);
        Assert.InRange(worker.ContinuationScore(previous, earlier, winner), 1, 6144);
        owner.NewGame();
        Assert.Equal(0, worker.ContinuationScore(previous, earlier, winner));
    }

    [Fact]
    public void SearchLearnsAndStrictNewGameReproducesTheSameResult()
    {
        using var state = GameState.InitialState();
        using var owner = new ParallelSearcher(4096);
        var first = owner.DepthBoundSearch(state, 7);
        Assert.Contains(first.move[0], state.LegalMoves);
        Assert.Contains(new ReadOnlySpan<short>(owner.Searchers[0].ContinuationHistory,
            Searcher.ContinuationHistoryLength).ToArray(), value => value != 0);
        owner.NewGame();
        new Span<Sapling.Engine.Transpositions.Transposition>(owner.Searchers[0].Transpositions, 4096).Clear();
        var second = owner.DepthBoundSearch(state, 7);
        Assert.Equal(first.score, second.score);
        Assert.Equal(first.nodes, second.nodes);
        Assert.Equal(first.move, second.move);
    }
}
