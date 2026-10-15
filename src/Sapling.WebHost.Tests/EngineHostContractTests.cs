using System.Text.RegularExpressions;
using FluentAssertions;
using Sapling.Engine;
using Sapling.WebHost;

namespace Sapling.WebHost.Tests;

/// <summary>
/// Host contract tests: exercise the managed browser facade (compiled under native .NET) for the
/// budget/deadline handling (R4), PGN/SAN correctness (R6) and rejected-FEN safety (R7).
/// </summary>
public class EngineHostContractTests
{
    private static long Num(string json, string key)
    {
        var m = Regex.Match(json, "\"" + key + "\":(-?\\d+)");
        m.Success.Should().BeTrue($"key '{key}' missing in: {json}");
        return long.Parse(m.Groups[1].Value);
    }

    [Fact]
    public void SearchSlice_CapsToTotalNodeBudget()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition(Constants.InitialState);
        EngineHost.StartSearch(1, maxDepth: 8, sliceNodes: 10000, totalNodes: 1, timeMs: 0);

        var slice = EngineHost.SearchSlice(1);

        slice.Should().Contain("\"ok\":true");
        slice.Should().Contain("\"done\":true");
        Num(slice, "nodesTotal").Should().BeLessThan(100, "the slice must be capped to the remaining budget, not run 10000 nodes");
        slice.Should().NotContain("\"bestMove\":\"\""); // a legal fallback move is preserved
    }

    [Fact]
    public void Pgn_DisambiguatesPieceMove_AsPieceLetterThenFile()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition("4k3/8/8/8/8/8/8/1N2KN2 w - - 0 1");
        EngineHost.ApplyMove("b1d2").Should().Contain("\"ok\":true");

        var pgn = EngineHost.Pgn();

        pgn.Should().Contain("Nbd2");
        pgn.Should().NotContain("bNd2");
        pgn.Should().Contain("1. Nbd2");
    }

    [Fact]
    public void Pgn_UsesCanonicalMoveNumbers()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition(Constants.InitialState);
        EngineHost.ApplyMove("e2e4");
        EngineHost.ApplyMove("e7e5");
        EngineHost.ApplyMove("g1f3");

        var pgn = EngineHost.Pgn();

        pgn.Should().Contain("1. e4 e5 2. Nf3");
    }

    [Fact]
    public void SetPosition_RejectedFen_LeavesStateUnchanged()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition("4k3/8/8/8/8/8/8/4K3 w - - 0 1").Should().Contain("\"ok\":true");

        EngineHost.SetPosition("not a fen").Should().Contain("\"ok\":false");

        var pgn = EngineHost.Pgn();
        pgn.Should().Contain("\"ok\":true");
        pgn.Should().Contain("4k3/8/8/8/8/8/8/4K3"); // start FEN preserved, not corrupted
    }

    [Fact]
    public void SetPosition_RejectsMissingKingFen()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition("4k3/8/8/8/8/8/8/8 w - - 0 1").Should().Contain("\"ok\":false");
    }
}
