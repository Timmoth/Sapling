using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

/// <summary>
/// Contract test for the quiet-history update: the cutoff move gets the bonus, only quiet
/// moves that were actually searched get the malus, and a quiet that was never searched is untouched.
/// </summary>
public unsafe class QuietHistoryTests
{
    private const int HistoryLength = 13 * 64;

    [Fact]
    public void MalusAppliesOnlyToSearchedQuiets()
    {
        using var state = GameState.InitialState();
        var quiets = state.LegalMoves.Where(m => m.IsQuiet()).Take(4).ToList();
        Assert.Equal(4, quiets.Count);

        var winner = quiets[0];
        var searched = new[] { quiets[1], quiets[2] };
        var notSearched = quiets[3];

        var history = stackalloc int[HistoryLength];
        new Span<int>(history, HistoryLength).Clear();

        fixed (uint* sq = searched)
            HistoryHeuristicExtensions.UpdateMovesHistory(history, sq, searched.Length, winner, 5);

        Assert.True(history[winner.GetCounterMoveIndex()] > 0);
        Assert.True(history[searched[0].GetCounterMoveIndex()] < 0);
        Assert.True(history[searched[1].GetCounterMoveIndex()] < 0);
        Assert.True(history[notSearched.GetCounterMoveIndex()] == 0, "unsearched quiet is untouched");
    }

    [Theory]
    [InlineData(2, 7000, 0.0f, 5, 2)]
    [InlineData(2, 6000, 0.0001f, 5, 1)]
    [InlineData(2, -6000, 0.0001f, 5, 3)]
    [InlineData(3, -10000, 0.0005f, 5, 5)]
    [InlineData(3, 10000, 0.0005f, 5, 0)]
    public void HistoryAdjustsQuietMoveReduction(int reduction, int history, float scale, int expectedMaximum,
        int expected)
    {
        Assert.Equal(expected, HistoryHeuristicExtensions.AdjustLateMoveReduction(reduction, history, scale, expectedMaximum));
    }
}
