using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

public unsafe class MoveContextHistoryTests
{
    [Fact]
    public void ContextsSeparateRepliesAndPawnStructuresAndIgnoreNullPredecessors()
    {
        using var owner = new ParallelSearcher(1024);
        var worker = owner.Searchers[0];
        var previous = MoveExtensions.EncodeNormalMove(Constants.BlackKnight, 57, 42);
        var otherPrevious = MoveExtensions.EncodeNormalMove(Constants.BlackKnight, 62, 45);
        var winner = MoveExtensions.EncodeNormalMove(Constants.WhiteKnight, 1, 18);
        worker.UpdateQuietContexts(previous, 123, winner, null, 0, 5);
        Assert.True(worker.ContinuationHistory[Searcher.ContinuationIndex(previous, winner)] > 0);
        Assert.Equal(0, worker.ContinuationHistory[Searcher.ContinuationIndex(otherPrevious, winner)]);
        Assert.True(worker.QuietContextScore(previous, 123, winner) >
            worker.QuietContextScore(otherPrevious, 124, winner));
        worker.UpdateQuietContexts(0, 124, winner, null, 0, 5);
        Assert.Equal(0, worker.ContinuationHistory[Searcher.ContinuationIndex(0, winner)]);
        Assert.True(worker.PawnMoveHistory[Searcher.PawnMoveIndex(124, winner)] > 0);
        owner.NewGame();
        Assert.Equal(0, worker.QuietContextScore(previous, 123, winner));
    }

    [Fact]
    public void RealSearchPopulatesBothContextTables()
    {
        using var state = GameState.InitialState();
        using var owner = new ParallelSearcher(4096);
        var result = owner.DepthBoundSearch(state, 7);
        Assert.Contains(result.move[0], state.LegalMoves);
        var worker = owner.Searchers[0];
        Assert.Contains(new ReadOnlySpan<short>(worker.ContinuationHistory,
            Searcher.ContinuationHistoryLength).ToArray(), value => value != 0);
        Assert.Contains(new ReadOnlySpan<short>(worker.PawnMoveHistory,
            Searcher.PawnMoveHistoryLength).ToArray(), value => value != 0);
    }
}
