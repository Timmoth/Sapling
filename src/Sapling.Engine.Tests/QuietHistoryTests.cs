using FluentAssertions;
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
        quiets.Count.Should().Be(4);

        var winner = quiets[0];
        var searched = new[] { quiets[1], quiets[2] };
        var notSearched = quiets[3];

        var history = stackalloc int[HistoryLength];
        new Span<int>(history, HistoryLength).Clear();

        fixed (uint* sq = searched)
            HistoryHeuristicExtensions.UpdateMovesHistory(history, sq, searched.Length, winner, 5);

        history[winner.GetCounterMoveIndex()].Should().BePositive("cutoff move gets the bonus");
        history[searched[0].GetCounterMoveIndex()].Should().BeNegative("searched quiet gets malus");
        history[searched[1].GetCounterMoveIndex()].Should().BeNegative("searched quiet gets malus");
        history[notSearched.GetCounterMoveIndex()].Should().Be(0, "unsearched quiet is untouched");
    }
}
