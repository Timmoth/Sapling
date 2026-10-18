using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sapling.Engine.Tests;

/// <summary>
/// Process-level tournament/production stability checks driven through the real
/// UCI surface of the Sapling app binary. Serialized in one collection so SMP
/// engines don't oversubscribe the machine in parallel.
/// </summary>
public class UciProcessStabilityTests
{
    private const string ErrorPrefix = "info string error";

    private static string? FindEngine()
    {
        var env = Environment.GetEnvironmentVariable("SAPLING_EXE");
        if (env is not null && File.Exists(env)) return env;
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Sapling.exe" : "Sapling";
        var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var targetFramework = Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        foreach (var config in new[] { "Release", "Debug" })
        {
            var output = Path.Combine(src, "Sapling", "bin", config);
            var p = Path.Combine(output, targetFramework, exeName);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static UciProcess StartEngine()
    {
        var exe = FindEngine();
        Assert.True(exe != null, "the Sapling app must be built (dotnet test on the solution builds it)");
        return new UciProcess(exe!);
    }

    private static void ReplayLegal(string moves)
    {
        using var e = StartEngine();
        e.Send($"position startpos moves {moves}");
        e.Ready();
    }

    [Fact]
    public void TimeControls_AllVariantsProduceBestmove()
    {
        using var e = StartEngine();
        e.Send("position startpos");
        foreach (var go in new[]
        {
            "go movetime 100",
            "go wtime 10000 btime 10000 winc 100 binc 100",
            "go wtime 50 btime 50 winc 0 binc 0",
            "go wtime 1 btime 1 winc 0 binc 0",
            "go nodes 2000",
            "go depth 5",
        })
        {
            Assert.True(e.Bestmove(go).Length > 0, $"go '{go}' must produce a bestmove");
        }
        e.Send("go infinite");
        Assert.False(e.SawPrefixWithin("bestmove", TimeSpan.FromMilliseconds(300)), "go infinite must keep searching until an explicit stop");
        e.Send("stop");
        Assert.True(e.Bestmove("").Length > 0, "infinite+stop must produce a bestmove");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(24)]
    public void Smp_ThreadCounts_ProduceLegalBestmoves(int threads)
    {
        using var e = StartEngine();
        e.Send($"setoption name Threads value {threads}");
        e.Ready();
        e.Send("position startpos");
        var mv = e.Bestmove("go movetime 250");
        Assert.True(mv.Length > 0, $"Threads={threads} must produce a bestmove");
        e.Send($"position startpos moves {mv}");
        e.Ready();
    }

    [Theory]
    [InlineData(32)]
    [InlineData(256)]
    [InlineData(1024)]
    public void Hash_Sizes_Work(int mb)
    {
        using var e = StartEngine();
        e.Send($"setoption name Hash value {mb}");
        e.Ready();
        e.Send("position startpos");
        Assert.True(e.Bestmove("go depth 6").Length > 0, $"Hash={mb} must produce a bestmove");
    }

    [Fact]
    public void Options_AllAdvertisedOptionsAtBoundaries()
    {
        using var e = StartEngine();
        var options = e.ParseOptions();
        Assert.Contains("Threads", options);
        Assert.Contains("Hash", options);
        foreach (var (name, (kind, lo, hi, dflt)) in options)
        {
            if (kind == "spin")
            {
                // Hash's advertised max (1 TB) is a known hang; probe a sane ceiling.
                var max = hi;
                if (name == "Hash" && max is not null && int.Parse(max) > 8192) max = "4096";
                foreach (var val in new[] { lo, max, dflt })
                {
                    if (val is null) continue;
                    e.Send($"setoption name {name} value {val}");
                    e.Ready();
                }
            }
            else if (kind == "check")
            {
                foreach (var val in new[] { "true", "false" })
                {
                    e.Send($"setoption name {name} value {val}");
                    e.Ready();
                }
            }
            else if (kind == "string")
            {
                e.Send($"setoption name {name} value {dflt ?? "default"}");
                e.Ready();
            }
        }
        e.Send("position startpos");
        Assert.True(e.Bestmove("go depth 4").Length > 0, "engine must still search after option stress");
    }

    [Fact]
    public void FullSelfPlayGame_ClocksAndLegality()
    {
        using var e = StartEngine();
        var moves = new List<string>();
        int wtime = 400, btime = 400;
        for (var ply = 0; ply < 40; ply++)
        {
            var white = ply % 2 == 0;
            var clock = white ? wtime : btime;
            var sw = Stopwatch.StartNew();
            var mv = e.Bestmove($"go wtime {wtime} btime {btime} winc 4 binc 4");
            var dt = (int)sw.ElapsedMilliseconds;
            if (mv.Length == 0 || mv == "(none)") break;
            Assert.True(dt < clock + 500, $"ply {ply} overran its clock");
            if (white) wtime = Math.Max(1, wtime - dt + 4); else btime = Math.Max(1, btime - dt + 4);
            moves.Add(mv);
            e.Send("position startpos moves " + string.Join(' ', moves));
        }
        e.Ready();
        Assert.True(moves.Count >= 20);
        ReplayLegal(string.Join(' ', moves));
    }

    [Fact]
    public void GameOver_MatedSideReturnsNone()
    {
        using var e = StartEngine();
        e.Send("position fen 6k1/6Q1/6K1/8/8/8/8/8 b - - 0 1");
        Assert.True(e.Bestmove("go depth 5") == "(none)", "a mated side must report bestmove (none)");
    }

    [Fact]
    public void GameOver_FlaggedAndNegativeClocksStillAnswer()
    {
        using var e = StartEngine();
        e.Send("position startpos");
        Assert.NotEmpty(e.Bestmove("go wtime 0 btime 5000"));
        Assert.NotEmpty(e.Bestmove("go wtime -100 btime 5000"));
        e.Ready();
    }

    [Fact]
    public void InvalidInput_ErrorsAndEngineStaysAlive()
    {
        using var e = StartEngine();
        e.Send("position startpos moves e2e5");
        Assert.Contains("illegal move", e.Expect(ErrorPrefix));
        e.Ready();
        e.Send("position");
        e.Expect(ErrorPrefix);
        e.Ready();
        e.Send("position startpos junk"); // silently ignored (observed); must stay usable
        e.Ready();
        e.Send("position startpos");
        Assert.NotEmpty(e.Bestmove("go depth 4"));
    }

    [Fact]
    public void InfoFormat_FieldsPresentAndPvMatchesBestmove()
    {
        using var e = StartEngine();
        e.Send("position startpos");
        var (mv, lines) = e.BestmoveWithLines("go depth 10");
        var infos = lines.Where(l => l.StartsWith("info depth ")).ToList();
        Assert.NotEmpty(infos);
        var last = infos[^1].Split(' ');
        foreach (var field in new[] { "score", "nodes", "nps", "time", "pv" })
            Assert.True(last.Contains(field), $"info line must carry {field}");
        var pv = last[(Array.IndexOf(last, "pv") + 1)..];
        Assert.NotEmpty(pv);
        Assert.True(pv[0] == mv, "pv head must equal bestmove");
        var depths = infos.Select(l => int.Parse(l.Split()[2])).ToList();
        Assert.Equal(depths.OrderBy(x => x), depths);
        Assert.True(depths[^1] == 10, "go depth 10 must reach depth 10");
    }

    [Fact]
    public void Lifecycle_NewgameCyclesAndStopRace()
    {
        using var e = StartEngine();
        for (var game = 0; game < 3; game++)
        {
            e.Send("ucinewgame");
            e.Ready();
            e.Send("position startpos");
            Assert.True(e.Bestmove("go depth 6").Length > 0, $"game {game}");
        }
        e.Send("position startpos");
        e.Send("go depth 90");
        e.Send("stop");
        Assert.True(e.Bestmove("").Length > 0, "stop racing search start must still answer");
    }

    [Fact]
    public void Endurance_MultipleGamesSingleProcess()
    {
        using var e = StartEngine();
        var moves = new List<string>();
        for (var game = 0; game < 5; game++)
        {
            e.Send("ucinewgame");
            e.Ready();
            moves.Clear();
            for (var ply = 0; ply < 10; ply++)
            {
                e.Send("position startpos moves " + string.Join(' ', moves));
                var mv = e.Bestmove("go depth 5");
                if (mv.Length == 0 || mv == "(none)") break;
                moves.Add(mv);
            }
        }
        e.Ready();
        ReplayLegal(string.Join(' ', moves));
    }

    [Fact]
    public void HumanEloAndPonder_OptionsProduceLegalPlay()
    {
        using var e = StartEngine();
        e.Send("setoption name HumanElo value 1500");
        e.Ready();
        e.Send("position startpos");
        var mv = e.Bestmove("go movetime 200");
        Assert.NotEmpty(mv);
        e.Send($"position startpos moves {mv}");
        e.Ready();
        e.Send("setoption name HumanElo value 0");
        e.Send("setoption name Ponder value true");
        e.Ready();
        e.Send("position startpos");
        Assert.NotEmpty(e.Bestmove("go wtime 2000 btime 2000"));
    }

    [Fact]
    public void PonderCommands_DoNotWedgeTheEngine()
    {
        // The engine doesn't truly ponder, but GUIs may still send
        // 'go ponder' / 'ponderhit'; both must stay non-fatal and produce
        // legal play.
        using var e = StartEngine();
        e.Send("setoption name Ponder value true");
        e.Ready();
        e.Send("position startpos");
        e.Send("go ponder wtime 5000 btime 5000");
        Assert.False(e.SawPrefixWithin("bestmove", TimeSpan.FromMilliseconds(250)), "ponder search should continue until ponderhit or stop");
        e.Send("ponderhit");
        var mv = e.Bestmove("");
        Assert.True(mv.Length > 0, "ponderhit must release the completed ponder result");
        e.Send($"position startpos moves {mv}");
        e.Ready();
        Assert.NotEmpty(e.Bestmove("go wtime 5000 btime 5000"));
    }

    [Fact]
    public void Hash_AtAdvertisedMax_ShouldNotHang()
    {
        using var e = StartEngine();
        e.Send("uci");
        var dump = e.ReadUntil("uciok", TimeSpan.FromSeconds(10));
        var hashLine = Assert.Single(dump, l => l.StartsWith("option name Hash"));
        var max = int.Parse(hashLine.Split("max")[1].Trim());
        Assert.True(max <= 1_000_000);
        e.Send($"setoption name Hash value {max}");
        e.Ready();
    }
}

internal sealed class UciProcess : IDisposable
{
    private readonly Process _p;
    private readonly BlockingCollection<string> _lines = [];
    private readonly List<string> _recent = [];

    public UciProcess(string exe)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Under `dotnet test` the child inherits the test host's startup hooks
        // (coverlet) and additional-deps, which wedge the engine process.
        psi.Environment.Remove("DOTNET_STARTUP_HOOKS");
        psi.Environment.Remove("DOTNET_ADDITIONAL_DEPS");
        psi.Environment.Remove("DOTNET_SHARED_STORE");
        _p = Process.Start(psi)!;
        var reader = new Thread(() =>
        {
            try
            {
                while (_p.StandardOutput.ReadLine() is { } line)
                    _lines.Add(line.Trim());
            }
            catch { /* process died */ }
            _lines.Add("<exit>");
        })
        { IsBackground = true };
        reader.Start();
        var errReader = new Thread(() =>
        {
            try
            {
                while (_p.StandardError.ReadLine() is { } line)
                    _lines.Add("stderr: " + line.Trim());
            }
            catch { /* process died */ }
        })
        { IsBackground = true };
        errReader.Start();
        Send("uci");
        Expect("uciok", TimeSpan.FromSeconds(30));
    }

    public void Send(string msg)
    {
        _p.StandardInput.WriteLine(msg);
        _p.StandardInput.Flush();
    }

    private string Next(TimeSpan timeout)
    {
        if (!_lines.TryTake(out var line, timeout))
        {
            var seen = string.Join(" | ", _recent.TakeLast(15));
            throw new TimeoutError($"no line within {timeout.TotalSeconds}s; exe=[{_p.StartInfo.FileName}] exited={_p.HasExited} recent: [{seen}]");
        }
        _recent.Add(line);
        if (line == "<exit>")
            throw new InvalidOperationException("engine process exited");
        return line;
    }

    public string Expect(string prefix, TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(10);
        var deadline = DateTime.UtcNow + t;
        while (true)
        {
            var line = Next(deadline - DateTime.UtcNow);
            if (line.StartsWith(prefix)) return line;
        }
    }

    public List<string> ReadUntil(string prefix, TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(30);
        var deadline = DateTime.UtcNow + t;
        var lines = new List<string>();
        while (true)
        {
            var line = Next(deadline - DateTime.UtcNow);
            lines.Add(line);
            if (line.StartsWith(prefix)) return lines;
        }
    }

    public void Ready()
    {
        Send("isready");
        Expect("readyok", TimeSpan.FromSeconds(15));
    }

    public bool SawPrefixWithin(string prefix, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && _lines.TryTake(out var line, deadline - DateTime.UtcNow))
        {
            _recent.Add(line);
            if (line.StartsWith(prefix)) return true;
        }
        return false;
    }

    public string Bestmove(string go) => BestmoveWithLines(go).move;

    public (string move, List<string> lines) BestmoveWithLines(string go)
    {
        if (go.Length > 0) Send(go);
        var lines = ReadUntil("bestmove");
        var bm = lines.LastOrDefault(l => l.StartsWith("bestmove "))?.Split(' ');
        return (bm is not null && bm.Length > 1 ? bm[1] : "", lines);
    }

    public Dictionary<string, (string kind, string? lo, string? hi, string? dflt)> ParseOptions()
    {
        Send("uci");
        var opts = new Dictionary<string, (string, string?, string?, string?)>();
        foreach (var line in ReadUntil("uciok"))
        {
            if (!line.StartsWith("option name ")) continue;
            var name = line.Split()[2];
            var kind = line.Split(" type ")[1].Split()[0];
            string? After(string marker) => line.Contains(marker) ? line.Split(marker)[1].Split()[0] : null;
            opts[name] = (kind, After(" min "), After(" max "), After(" default "));
        }
        return opts;
    }

    public void Dispose()
    {
        try
        {
            if (!_p.HasExited)
            {
                Send("quit");
                if (!_p.WaitForExit(1000)) _p.Kill(true);
            }
        }
        catch { _p.Kill(true); }
        _p.Dispose();
    }
}

internal sealed class TimeoutError(string message) : Exception(message);
