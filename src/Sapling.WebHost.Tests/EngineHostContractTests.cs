using System.Text.RegularExpressions;
using System.Text.Json;
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
        Assert.True(m.Success, $"key '{key}' missing in: {json}");
        return long.Parse(m.Groups[1].Value);
    }

    [Fact]
    public void SearchSlice_CapsToTotalNodeBudget()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition(Constants.InitialState);
        EngineHost.StartSearch(1, maxDepth: 8, sliceNodes: 10000, totalNodes: 1, timeMs: 0);

        var slice = EngineHost.SearchSlice(1);

        Assert.Contains("\"ok\":true", slice);
        Assert.Contains("\"done\":true", slice);
        Assert.True(Num(slice, "nodesTotal") < 100, "the slice must be capped to the remaining budget, not run 10000 nodes");
        Assert.DoesNotContain("\"bestMove\":\"\"", slice); // a legal fallback move is preserved
    }

    [Fact]
    public void Pgn_DisambiguatesPieceMove_AsPieceLetterThenFile()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition("4k3/8/8/8/8/8/8/1N2KN2 w - - 0 1");
        Assert.Contains("\"ok\":true", EngineHost.ApplyMove("b1d2"));

        var pgn = EngineHost.Pgn();

        Assert.Contains("Nbd2", pgn);
        Assert.DoesNotContain("bNd2", pgn);
        Assert.Contains("1. Nbd2", pgn);
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

        Assert.Contains("1. e4 e5 2. Nf3", pgn);
    }

    [Fact]
    public void SetPosition_RejectedFen_LeavesStateUnchanged()
    {
        EngineHost.Initialize(1);
        Assert.Contains("\"ok\":true", EngineHost.SetPosition("4k3/8/8/8/8/8/8/4K3 w - - 0 1"));

        Assert.Contains("\"ok\":false", EngineHost.SetPosition("not a fen"));

        var pgn = EngineHost.Pgn();
        Assert.Contains("\"ok\":true", pgn);
        Assert.Contains("4k3/8/8/8/8/8/8/4K3", pgn); // start FEN preserved, not corrupted
    }

    [Fact]
    public void SetPosition_RejectsMissingKingFen()
    {
        EngineHost.Initialize(1);
        Assert.Contains("\"ok\":false", EngineHost.SetPosition("4k3/8/8/8/8/8/8/8 w - - 0 1"));
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

        Assert.Equal(expected, response.RootElement.GetProperty("humanElo").GetInt32());
        Assert.Equal(expected != 0, response.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void SearchSlice_WithHumanElo_ReturnsALegalMoveFromTheEmbeddedPolicyPath()
    {
        EngineHost.Initialize(1);
        const string fen = "4k3/8/8/8/8/8/8/4K3 w - - 0 1";
        using var position = JsonDocument.Parse(EngineHost.SetPosition(fen));
        var legalMoves = position.RootElement.GetProperty("legalMoves").EnumerateArray()
            .Select(item => item.GetString()).ToArray();
        Assert.Contains("\"enabled\":true", EngineHost.SetHumanElo(1500));
        Assert.Contains("\"ok\":true", EngineHost.StartSearch(73, maxDepth: 1, sliceNodes: 100, totalNodes: 100, timeMs: 0));

        string response = "";
        for (var i = 0; i < 10; i++)
        {
            response = EngineHost.SearchSlice(73);
            using var slice = JsonDocument.Parse(response);
            if (slice.RootElement.GetProperty("done").GetBoolean()) break;
        }

        using var result = JsonDocument.Parse(response);
        Assert.Contains(result.RootElement.GetProperty("bestMove").GetString(), legalMoves!);
        Assert.True(result.RootElement.GetProperty("done").GetBoolean());
    }

    [Fact]
    public void ScoreMove_ReturnsAMoveScoreWithoutApplyingTheMove()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition("4k3/8/8/8/8/8/8/4K3 w - - 0 1");

        using var score = JsonDocument.Parse(EngineHost.ScoreMove("e1d1"));

        Assert.True(score.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("e1d1", score.RootElement.GetProperty("move").GetString());
        Assert.Contains(score.RootElement.GetProperty("scoreType").GetString(), new[] { "cp", "mate" });
        Assert.InRange(score.RootElement.GetProperty("score").GetInt32(), -32000, 32000);
        Assert.Contains("4k3/8/8/8/8/8/8/4K3", EngineHost.Pgn());
        Assert.DoesNotContain("Kd1", EngineHost.Pgn());
        Assert.Contains("\"ok\":true", EngineHost.ApplyMove("e1d1"));
    }

    [Fact]
    public void ScoreMove_RejectsAnIllegalMove()
    {
        EngineHost.Initialize(1);
        EngineHost.SetPosition(Constants.InitialState);

        Assert.Contains("\"ok\":false", EngineHost.ScoreMove("e2e5"));
        Assert.Contains("\"illegal\"", EngineHost.ScoreMove("e2e5"));
    }
}
