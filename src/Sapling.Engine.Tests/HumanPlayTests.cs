using FluentAssertions;
using Sapling.Engine;
using Sapling.Engine.Search;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Tests;

/// <summary>
/// Human-like difficulty (continuous Elo, gap/T + priors). Guards: disabled/max-Elo always play the
/// best move; confidence (a large gap is less likely overridden than a small one); lower Elo deviates
/// more than higher Elo; move-type priors bias equal-score moves; and root ranking is a correct
/// best-first independent pass with feature flags.
/// </summary>
public class HumanPlayTests
{
    private static int BestCount(Random rng, int[] scores, int[] feats, int elo, int draws)
    {
        HumanPlay.SetElo(elo);
        var zeros = 0;
        for (var i = 0; i < draws; i++)
            if (HumanPlay.PickRank(rng, scores, feats) == 0) zeros++;
        return zeros;
    }

    [Fact]
    public void Disabled_AlwaysBestMove()
    {
        HumanPlay.Reset();
        HumanPlay.Enabled.Should().BeFalse();
        var rng = new Random(1);
        var s = new[] { 100, 0, 0, 0 };
        var f = new int[4];
        for (var i = 0; i < 50; i++)
            HumanPlay.PickRank(rng, s, f).Should().Be(0);
    }

    [Fact]
    public void MaxElo_AlwaysBestMove()
    {
        HumanPlay.Reset();
        var rng = new Random(2);
        var s = new[] { 50, 49, 48, 47 };
        BestCount(rng, s, new int[4], 3000, 50).Should().Be(50);
    }

    [Fact]
    public void Confidence_ObviousMoveLessOverridden()
    {
        HumanPlay.Reset();
        var rng = new Random(3);
        var obvious = BestCount(rng, new[] { 500, 0, 0, 0, 0 }, new int[5], 1200, 400);
        var subtle = BestCount(rng, new[] { 50, 49, 48, 47, 46 }, new int[5], 1200, 400);
        obvious.Should().BeGreaterThan(subtle);
    }

    [Fact]
    public void LowerElo_DeviatesMoreThanHigherElo()
    {
        HumanPlay.Reset();
        var rng = new Random(4);
        var s = new[] { 300, 0, 0, 0, 0 };
        var strong = BestCount(rng, s, new int[5], 2500, 400);
        var weak = BestCount(rng, s, new int[5], 300, 400);
        strong.Should().BeGreaterThan(weak);
    }

    [Fact]
    public void Priors_BiasEqualScoreMoves()
    {
        HumanPlay.Reset();
        var rng = new Random(5);
        // Equal scores; index 1 is a capture (feat bit0), index 0 quiet. The capture should win more
        // than the quiet best move at a mid Elo.
        var s = new[] { 0, 0 };
        var f = new[] { 0, 1 };
        HumanPlay.SetElo(1500);
        var captureWins = 0;
        for (var i = 0; i < 400; i++)
            if (HumanPlay.PickRank(rng, s, f) == 1) captureWins++;
        captureWins.Should().BeGreaterThan(250);
    }

    [Fact]
    public void RankRootMoves_BestFirst_MateFirst_WithFlags()
    {
        HumanPlay.Reset();
        using var state = GameState.InitialState();
        state.ResetToFen("6k1/5ppp/8/8/8/8/8/R6K w - - 0 1"); // Ra8#
        using var searcher = new ParallelSearcher(1024);

        var ranked = searcher.RankRootMoves(state, 2);

        ranked.Should().NotBeEmpty();
        ranked.Count.Should().Be(state.LegalMoves.Count);
        ranked[0].Score.Should().BeGreaterThan(1000); // mate score on top
        state.LegalMoves.Should().Contain(ranked[0].Move);
        for (var i = 1; i < ranked.Count; i++)
            ranked[i - 1].Score.Should().BeGreaterThanOrEqualTo(ranked[i].Score);
    }
}
