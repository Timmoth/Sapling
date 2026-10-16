using Sapling.Engine.Search;
using Xunit;

namespace Sapling.Engine.Tests;

public class TimeManagementTests
{
    [Fact]
    public void MoveTime_UsesRequestedBudgetWithoutClockOverhead()
    {
        var budget = TimeManagement.Calculate(0, 0, 0, 0, false, moveTimeMs: 500);

        Assert.Equal(new SearchTimeBudget(500, 500, false), budget);
    }

    [Fact]
    public void MissingClockAndMoveTime_IsUnbounded()
    {
        var budget = TimeManagement.Calculate(0, 0, 0, 0, false);

        Assert.Equal(SearchTimeBudget.InfiniteSearch, budget);
    }

    [Fact]
    public void FlaggedClock_GetsSmallFiniteBudget()
    {
        var budget = TimeManagement.Calculate(0, 0, 0, 0, true);

        Assert.False(budget.Infinite);
        Assert.True(budget.SoftLimitMs > 0);
        Assert.True(budget.HardLimitMs >= budget.SoftLimitMs);
    }

    [Theory]
    [InlineData(10_000, 0, 0)]
    [InlineData(10_000, 100, 10)]
    [InlineData(300_000, 1_000, 40)]
    [InlineData(-1, -1, -1)]
    public void ClockBudgets_ArePositiveAndHardLimitContainsSoft(int time, int increment, int movesToGo)
    {
        var budget = TimeManagement.Calculate(time, increment, movesToGo, 20, true);

        Assert.False(budget.Infinite);
        Assert.True(budget.SoftLimitMs > 0);
        Assert.True(budget.HardLimitMs >= budget.SoftLimitMs);
        Assert.True(budget.HardLimitMs <= Math.Max(1, time - 50));
    }

    [Fact]
    public void ExplicitMovesToGo_AllocatesMoreThanEmergencyClock()
    {
        var fiveMoves = TimeManagement.Calculate(60_000, 0, 5, 20, true);
        var thirtyMoves = TimeManagement.Calculate(60_000, 0, 30, 20, true);

        Assert.True(fiveMoves.SoftLimitMs > thirtyMoves.SoftLimitMs);
    }

    [Fact]
    public void SoftLimitAdjustment_SpendsLessOnStableMoves()
    {
        var unstable = TimeManagement.AdjustSoftLimit(1_000, 5_000, 10, 0, 0.5);
        var stable = TimeManagement.AdjustSoftLimit(1_000, 5_000, 10, 5, 0.5);

        Assert.True(unstable > stable);
    }

    [Fact]
    public void SoftLimitAdjustment_IgnoresScoreDrop()
    {
        var stableScore = TimeManagement.AdjustSoftLimit(1_000, 5_000, 10, 2, 0.5);
        var scoreDrop = TimeManagement.AdjustSoftLimit(1_000, 5_000, 10, 2, 0.5);

        Assert.Equal(stableScore, scoreDrop);
    }

    [Fact]
    public void SoftLimitAdjustment_NeverExceedsHardLimit()
    {
        var adjusted = TimeManagement.AdjustSoftLimit(10_000, 2_000, 10, 0, 0.1);

        Assert.Equal(2_000, adjusted);
    }
}
