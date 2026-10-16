using System;
using FluentAssertions;
using Sapling.Engine.Search;
using Xunit;

namespace Sapling.Engine.Tests;

public class LifecycleTests
{
    [Fact]
    public unsafe void ResetToFen_DiscardsPreviousGamesRepetitionHashes()
    {
        using var state = GameState.InitialState();
        state.HashHistory[1] = 123;
        state.HashHistory[2] = 456;
        state.ResetToFen("4k3/8/8/8/8/8/8/4K3 w - - 12 20");
        for (var i = 0; i < state.Board.TurnCount - 1; i++)
            Assert.Equal(0UL, state.HashHistory[i]);
        Assert.Equal(state.Board.Hash, state.HashHistory[state.Board.TurnCount - 1]);
    }

    [Fact]
    public void DisposedGameState_IsRejectedBeforeHistoryIsCopied()
    {
        var state = GameState.InitialState();
        using var owner = new ParallelSearcher(1024);
        state.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owner.DepthBoundSearch(state, 2));
    }

    [NativeThreadsFact]
    public void Dispose_StopsAndJoinsAnActiveSearch()
    {
        using var state = GameState.InitialState();
        var owner = new ParallelSearcher(1024);
        var worker = owner.Searchers[0];
        var search = System.Threading.Tasks.Task.Factory.StartNew(
            () => owner.DepthBoundSearch(state, 90),
            System.Threading.CancellationToken.None,
            System.Threading.Tasks.TaskCreationOptions.LongRunning,
            System.Threading.Tasks.TaskScheduler.Default);
        try
        {
            Assert.True(System.Threading.SpinWait.SpinUntil(
                () => System.Threading.Volatile.Read(ref worker.NodesVisited) >= 1024 || search.IsCompleted,
                TimeSpan.FromSeconds(10)), "Search did not start within ten seconds.");
            Assert.False(search.IsCompleted, "Fixture must exercise disposal during an active search.");
            var dispose = System.Threading.Tasks.Task.Run(owner.Dispose);
            Assert.True(dispose.Wait(TimeSpan.FromSeconds(10)), "Dispose did not join the search.");
            Assert.True(search.Wait(TimeSpan.FromSeconds(10)), "Search remained active after disposal.");
            Assert.Throws<ObjectDisposedException>(() => worker.Reset(state));
        }
        finally
        {
            owner.Stop();
            owner.Dispose();
            search.GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void DisposedOwners_RejectNewOperations()
    {
        using var state = GameState.InitialState();
        var owner = new ParallelSearcher(1024);
        var worker = owner.Searchers[0];
        owner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owner.SetThreads(1));
        Assert.Throws<ObjectDisposedException>(() => owner.NewGame());
        Assert.Throws<ObjectDisposedException>(() => owner.DepthBoundSearch(state, 2));
        Assert.Throws<ObjectDisposedException>(() => worker.Reset(state));
        Assert.Throws<ObjectDisposedException>(() => worker.ClearCorrectionHistory());
    }

    [Fact]
    public void VoteIndex_DistinguishesEveryFromToPair()
    {
        var indices = new System.Collections.Generic.HashSet<int>();
        for (uint from = 0; from < 64; from++)
        for (uint to = 0; to < 64; to++)
            Assert.True(indices.Add(ParallelSearcher.MoveFromToIndex((from << 4) | (to << 10))));
        Assert.Equal(4096, indices.Count);
    }

    [Fact]
    public void HistoryCounterLimit_RejectsMoveWithoutMutatingPosition()
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 65535"));
        var hash = state.Board.Hash;
        Assert.Throws<InvalidOperationException>(() => state.Apply(state.LegalMoves[0]));
        Assert.Equal(hash, state.Board.Hash);
        Assert.Empty(state.History);
    }

    [Fact]
    public void ParallelSearcher_CreateResizeDoubleDispose_NoThrow()
    {
        var ps = new ParallelSearcher(1024);
        try
        {
            ps.Searchers.Count.Should().Be(1);

            // Grow and shrink worker pool; removed searchers must be disposed without leaking.
            // SetThreads clamps to Environment.ProcessorCount, so target the max this host allows.
            var grow = Math.Min(4, Environment.ProcessorCount);
            ps.SetThreads(grow);
            ps.Searchers.Count.Should().Be(grow);
            ps.SetThreads(1);
            ps.Searchers.Count.Should().Be(1);
        }
        finally
        {
            ps.Dispose();
            ps.Dispose(); // idempotent
        }
    }

    [Fact]
    public void Searcher_DoubleDispose_NoThrow()
    {
        var ps = new ParallelSearcher(1024);
        try
        {
            var s = ps.Searchers[0];
            s.Dispose();
            s.Dispose(); // idempotent; must not double-free native buffers
        }
        finally
        {
            ps.Dispose();
        }
    }

    [Fact]
    public void ParallelSearcher_NonPositiveSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParallelSearcher(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParallelSearcher(-1));
    }

    [Fact]
    public unsafe void ClearCorrectionHistory_ZeroesAllFourTables()
    {
        // D-07: correction history is per-game and must be clearable on ucinewgame.
        var ps = new ParallelSearcher(1024);
        try
        {
            var s = ps.Searchers[0];
            s.PawnCorrHist[0] = 12345;
            s.WhiteMaterialCorrHist[0] = -6789;
            s.BlackMaterialCorrHist[Searcher.TableSize] = 999;
            s.PreviousMoveCorrHist[17] = 4321;

            s.ClearCorrectionHistory();

            s.PawnCorrHist[0].Should().Be(0);
            s.WhiteMaterialCorrHist[0].Should().Be(0);
            s.BlackMaterialCorrHist[Searcher.TableSize].Should().Be(0);
            s.PreviousMoveCorrHist[17].Should().Be(0);
        }
        finally
        {
            ps.Dispose();
        }
    }

    [Fact]
    public unsafe void QuietMoveHistory_ClearsAcrossSearchesAndOnNewGame()
    {
        using var state = GameState.InitialState();
        using var owner = new ParallelSearcher(1024);
        var searcher = owner.Searchers[0];
        searcher.History[0] = 100;
        searcher.History[1] = -101;

        searcher.Reset(state);

        Assert.Equal(0, searcher.History[0]);
        Assert.Equal(0, searcher.History[1]);

        owner.NewGame();

        Assert.Equal(0, searcher.History[0]);
        Assert.Equal(0, searcher.History[1]);
    }

    [Fact]
    public void Search_HighFullmoveFen_StaysInHashHistoryBounds()
    {
        // TurnCount is a ushort set from the FEN fullmove number and indexes HashHistory directly.
        // Before the 1.4 fix HashHistory held 800 entries, so fullmove 65000 wrote ~513 KB past it.
        var fen = "4k3/8/8/8/8/8/8/4K3 w - - 0 65000";
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
        var ps = new ParallelSearcher(1024);
        try
        {
            var (move, _, _, _, _) = ps.DepthBoundSearch(state, 3);
            move.Should().NotBeEmpty("a legal move must be returned for a high-fullmove position");
        }
        finally
        {
            ps.Dispose();
        }
    }
}
