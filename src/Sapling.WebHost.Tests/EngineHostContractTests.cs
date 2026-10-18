using System.Text.RegularExpressions;
using System.Text.Json;
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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(300, 800)]
    [InlineData(1500, 1500)]
    [InlineData(3000, 2200)]
    public void SetHumanElo_UsesEmbeddedPolicyRange(int requested, int expected)
    {
        EngineHost.Initialize(1);

        using var response = JsonDocument.Parse(EngineHost.SetHumanElo(requested));

        response.RootElement.GetProperty("humanElo").GetInt32().Should().Be(expected);
        response.RootElement.GetProperty("enabled").GetBoolean().Should().Be(expected != 0);
    }

    [Fact]
    public void SearchSlice_WithHumanElo_ReturnsALegalMoveFromTheEmbeddedPolicyPath()
    {
        EngineHost.Initialize(1);
        const string fen = "4k3/8/8/8/8/8/8/4K3 w - - 0 1";
        using var position = JsonDocument.Parse(EngineHost.SetPosition(fen));
        var legalMoves = position.RootElement.GetProperty("legalMoves").EnumerateArray()
            .Select(item => item.GetString()).ToArray();
        EngineHost.SetHumanElo(1500).Should().Contain("\"enabled\":true");
        EngineHost.StartSearch(73, maxDepth: 1, sliceNodes: 100, totalNodes: 100, timeMs: 0)
            .Should().Contain("\"ok\":true");

        string response = "";
        for (var i = 0; i < 10; i++)
        {
            response = EngineHost.SearchSlice(73);
            using var slice = JsonDocument.Parse(response);
            if (slice.RootElement.GetProperty("done").GetBoolean()) break;
        }

        using var result = JsonDocument.Parse(response);
        result.RootElement.GetProperty("bestMove").GetString().Should().BeOneOf(legalMoves!);
        result.RootElement.GetProperty("done").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ScoreMove_ReturnsAMoveScoreWithoutApplyingTheMove()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition("4k3/8/8/8/8/8/8/4K3 w - - 0 1");

        using var score = JsonDocument.Parse(EngineHost.ScoreMove("e1d1"));

        score.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        score.RootElement.GetProperty("move").GetString().Should().Be("e1d1");
        score.RootElement.GetProperty("scoreType").GetString().Should().BeOneOf("cp", "mate");
        score.RootElement.GetProperty("score").GetInt32().Should().BeInRange(-32000, 32000);
        EngineHost.Pgn().Should().Contain("4k3/8/8/8/8/8/8/4K3").And.NotContain("Kd1");
        EngineHost.ApplyMove("e1d1").Should().Contain("\"ok\":true");
    }

    [Fact]
    public void ScoreMove_RejectsAnIllegalMove()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition(Constants.InitialState);

        EngineHost.ScoreMove("e2e5").Should().Contain("\"ok\":false").And.Contain("\"illegal\"");
    }
}
