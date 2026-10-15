using FluentAssertions;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

/// <summary>
/// Higher-level / end-to-end engine stability and correctness. These drive the public search API
/// the way the UCI layer and the browser host do, and assert behavioural invariants that unit-level
/// tests cannot: the engine must always return a legal move, must be deterministic single-threaded,
/// must actually deliver the mates it claims, and must never crash under random legal play.
/// </summary>
public class SearchStabilityTests
{
    private static GameState StateFromFen(string fen) =>
        new(BoardStateExtensions.CreateBoardFromFen(fen));

    [Theory]
    // Quiet, pinned, en-passant, castling, promotion and check positions: the returned best move
    // must always be a member of the legal move set for the position.
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 5 3")]
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1")]
    [InlineData("4k3/P7/8/8/8/8/8/4K3 w - - 0 1")]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1")]
    [InlineData("r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/2N2N2/PPPP1PPP/R1BQK2R w KQkq - 6 4")]
    public void BestMove_IsAlwaysLegal(string fen)
    {
        using var state = StateFromFen(fen);
        using var searcher = new ParallelSearcher(1024);

        var result = searcher.DepthBoundSearch(state, 8);

        result.move.Should().NotBeEmpty();
        state.LegalMoves.Should().Contain(result.move[0],
            $"best move {result.move[0].ToUciMoveName()} must be legal in {fen}");
    }

    [Fact]
    public void SingleThreadSearch_IsDeterministic_ForTheSamePositionAndDepth()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 5 3";

        using var stateA = StateFromFen(fen);
        using var searcherA = new ParallelSearcher(4096);
        var first = searcherA.DepthBoundSearch(stateA, 9);

        using var stateB = StateFromFen(fen);
        using var searcherB = new ParallelSearcher(4096);
        var second = searcherB.DepthBoundSearch(stateB, 9);

        first.move[0].Should().Be(second.move[0]);
        first.score.Should().Be(second.score);
        first.depthSearched.Should().Be(second.depthSearched);
    }

    [Theory]
    // Positions where the side to move has a mate in one. The engine must report a mate score and
    // the move it plays must actually leave the opponent with no legal moves while in check.
    [InlineData("6k1/5ppp/8/8/8/8/8/R6K w - - 0 1")]
    [InlineData("6k1/5ppp/8/8/8/8/8/1R4K1 w - - 0 1")]
    [InlineData("6k1/5ppp/8/8/8/8/8/Q6K w - - 0 1")]
    public void MateInOne_IsFoundAndActuallyDeliversMate(string fen)
    {
        using var state = StateFromFen(fen);
        using var searcher = new ParallelSearcher(1024);

        var result = searcher.DepthBoundSearch(state, 6);

        result.move.Should().NotBeEmpty();
        MoveScoring.IsMateScore(result.score).Should().BeTrue();
        result.score.Should().BePositive("the side to move is the one delivering mate");

        state.Apply(result.move[0]);
        state.Board.InCheck.Should().BeTrue("the mating move must give check");
        state.LegalMoves.Should().BeEmpty("the opponent must be checkmated");
    }

    [Fact]
    public void TimeBoundSearch_ReturnsALegalMoveWithinABoundedWallTime()
    {
        using var state = StateFromFen("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 5 3");
        using var searcher = new ParallelSearcher(4096);

        var result = searcher.TimeBoundSearch(state, 150);

        result.pv.Should().NotBeEmpty();
        state.LegalMoves.Should().Contain(result.pv[0]);
        result.duration.Should().BeLessThan(TimeSpan.FromSeconds(3),
            "a 150ms budget must not run away");
    }

    [Fact]
    public void NodeBoundSearch_RespectsTheNodeBudgetAndReturnsALegalMove()
    {
        using var state = StateFromFen("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 5 3");
        using var searcher = new ParallelSearcher(4096);

        var result = searcher.NodeBoundSearch(state, nodeLimit: 5000);

        result.pv.Should().NotBeEmpty();
        state.LegalMoves.Should().Contain(result.pv[0]);
        result.nodes.Should().BeLessThanOrEqualTo(5001,
            "single-thread node limits are checked at each recursive node and should overshoot by at most one");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void MultiThreadSearch_ReturnsALegalMove(int threads)
    {
        using var state = StateFromFen("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 5 3");
        using var searcher = new ParallelSearcher(4096);
        searcher.SetThreads(threads);

        var result = searcher.DepthBoundSearch(state, 8);

        result.move.Should().NotBeEmpty();
        state.LegalMoves.Should().Contain(result.move[0]);
    }

    [Fact]
    public void StalematePosition_HasNoLegalMovesAndSearchDoesNotCrash()
    {
        // Black king on h8 is stalemated: no legal moves, not in check.
        using var state = StateFromFen("7k/8/6Q1/8/8/8/8/K7 b - - 0 1");

        state.LegalMoves.Should().BeEmpty();
        state.Board.InCheck.Should().BeFalse();

        using var searcher = new ParallelSearcher(1024);
        var act = () => searcher.DepthBoundSearch(state, 5);
        act.Should().NotThrow();
    }

    [Fact]
    public void RandomLegalPlay_StaysStableAndAlwaysReturnsALegalMove()
    {
        // Seeded so the sequence is reproducible across runs and platforms.
        var rng = new Random(20261012);
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(4096);

        for (var ply = 0; ply < 60 && !state.GameOver(); ply++)
        {
            state.LegalMoves.Should().NotBeEmpty("a non-terminal position must have a legal move");

            var move = state.LegalMoves[rng.Next(state.LegalMoves.Count)];
            state.Apply(move);

            if (ply % 12 == 0)
            {
                var result = searcher.DepthBoundSearch(state, 6);
                result.move.Should().NotBeEmpty();
                state.LegalMoves.Should().Contain(result.move[0],
                    $"after {ply + 1} random plies the engine must still return a legal move");
            }
        }
    }
}
