using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

// Cross-platform qualification harness. Modes: info, bench, perft.

internal static class Program
{
    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "bench";
        var opts = ParseOptions(args.Skip(1).ToArray());

        switch (mode)
        {
            case "magics":
                WriteJson(new {
                    rook = Sapling.Engine.MoveGen.AttackTables.RookMagics.Select(m => m.MagicNumber).ToArray(),
                    bishop = Sapling.Engine.MoveGen.AttackTables.BishopMagics.Select(m => m.MagicNumber).ToArray()
                }, opts);
                break;
            case "info":
                WriteJson(BuildHeader(opts, Array.Empty<string>()), opts);
                break;
            case "bench":
                RunBench(opts);
                break;
            case "perft":
                return RunPerft(opts);
            default:
                Console.Error.WriteLine($"unknown mode: {mode}");
                return 2;
        }

        return 0;
    }

    private static readonly string[] KnownOptions =
    {
        "--out", "--source-id", "--corpus", "--depth", "--tt-mb", "--tt-reset",
        "--repeat", "--strict-reset", "--workers", "--engine",
    };

    private static Dictionary<string, string> ParseOptions(string[] a)
    {
        var d = new Dictionary<string, string>();
        for (var i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--"))
            {
                Console.Error.WriteLine($"unexpected positional argument: {a[i]}");
                Environment.Exit(2);
            }
            if (!KnownOptions.Contains(a[i]))
            {
                Console.Error.WriteLine($"unknown option: {a[i]}");
                Environment.Exit(2);
            }
            if (i + 1 >= a.Length)
            {
                Console.Error.WriteLine($"option {a[i]} requires a value");
                Environment.Exit(2);
            }
            if (a[i + 1].StartsWith("--") || d.ContainsKey(a[i]))
                throw new ArgumentException($"Missing value or duplicate option: {a[i]}");
            d[a[i]] = a[++i];
        }

        return d;
    }

    // Source identity is recorded from git when available, but remains informational:
    // it is not a cryptographically verified source tree. Assembly hashes are the
    // binding artifact identity.
    private static object CaptureSourceIdentity(string callerLabel)
    {
        string commit = "UNAVAILABLE", branch = "UNAVAILABLE";
        bool? dirty = null;
        string method = "caller label only (git unavailable)";
        try
        {
            commit = RunGit("rev-parse HEAD");
            branch = RunGit("rev-parse --abbrev-ref HEAD");
            dirty = RunGit("status --porcelain").Length > 0;
            method = "git metadata + caller label; not cryptographically verified";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"git source capture failed: {ex.Message}");
        }

        return new { sourceId = callerLabel, gitCommit = commit, gitBranch = branch, gitDirty = dirty, sourceIdMethod = method };
    }

    private static string RunGit(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p == null)
            throw new InvalidOperationException("could not start git");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments} failed (exit {p.ExitCode})");
        return output.Trim();
    }

    private static object BuildHeader(Dictionary<string, string> opts, string[] corpusHashes)
    {
        return new
        {
            schema = "sapling-qualification/2",
            build = new
            {
                source = CaptureSourceIdentity(GetOpt(opts, "--source-id", "UNSPECIFIED")),
                engineSha256 = Sha256(typeof(Searcher).Assembly.Location),
                harnessSha256 = Sha256(typeof(Program).Assembly.Location),
                nnueBackend = EngineCapabilities.NnueBackend,
                attacksBackend = EngineCapabilities.UsePext ? "Pext" : "Magic",
            },
            utcTimestamp = DateTime.UtcNow.ToString("O"),
            machine = new
            {
                os = RuntimeInformation.OSDescription,
                osArch = RuntimeInformation.OSArchitecture.ToString(),
                processArch = RuntimeInformation.ProcessArchitecture.ToString(),
                cpuCount = Environment.ProcessorCount,
                isa = new
                {
                    sse = Sse.IsSupported,
                    popcnt = Popcnt.IsSupported,
                    bmi1 = Bmi1.IsSupported,
                    bmi2 = Bmi2.IsSupported,
                    avx2 = Avx2.IsSupported,
                    avx512f = Avx512F.IsSupported,
                    avx512bw = Avx512BW.IsSupported,
                },
            },
            runtime = new
            {
                framework = Environment.Version.ToString(),
                stopwatchFrequency = Stopwatch.Frequency,
            },
            options = opts,
            corpusHashes,
        };
    }

    private static string GetOpt(Dictionary<string, string> o, string key, string fallback) =>
        o.TryGetValue(key, out var v) ? v : fallback;

    private static string ResolveCorpus(Dictionary<string, string> opts, string key, string defaultRel)
    {
        var p = GetOpt(opts, key, defaultRel);
        if (File.Exists(p))
        {
            return p;
        }

        var alt = Path.Combine("src", "Sapling.Bench", "fixtures", Path.GetFileName(defaultRel));
        if (!opts.ContainsKey(key) && File.Exists(alt))
        {
            return alt;
        }

        Console.Error.WriteLine($"corpus not found: {p}");
        Environment.Exit(2);
        return p;
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
    }

    private static void WriteJson(object payload, Dictionary<string, string> opts)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        if (opts.TryGetValue("--out", out var outPath))
        {
            File.WriteAllText(outPath, json);
        }
        else
        {
            Console.WriteLine(json);
        }
    }

    private static List<(string fen, string tag)> LoadBenchCorpus(string corpusPath)
    {
        var positions = File.ReadAllLines(corpusPath)
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
            .Select(l =>
            {
                var parts = l.Split('|');
                return (fen: parts[0].Trim(), tag: parts.Length > 1 ? parts[1].Trim() : "");
            })
            .ToList();

        if (positions.Count == 0)
        {
            Console.Error.WriteLine($"corpus contains no fixtures: {corpusPath}");
            Environment.Exit(2);
        }

        var duplicates = positions.GroupBy(p => p.fen).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            Console.Error.WriteLine($"corpus contains duplicate FENs ({duplicates.Count}): {string.Join(", ", duplicates.Take(5))}");
            Environment.Exit(2);
        }

        return positions;
    }

    private static unsafe void RunBench(Dictionary<string, string> opts)
    {
        var corpusPath = ResolveCorpus(opts, "--corpus", "fixtures/bench-corpus-v1.txt");
        var depth = int.Parse(GetOpt(opts, "--depth", "14"));
        var ttMb = int.Parse(GetOpt(opts, "--tt-mb", "256"));
        var ttReset = GetOpt(opts, "--tt-reset", "fresh");
        var repeat = int.Parse(GetOpt(opts, "--repeat", "1"));
        if (!bool.TryParse(GetOpt(opts, "--strict-reset", "false"), out var strictReset))
            throw new ArgumentException("--strict-reset must be true or false");
        var workers = int.Parse(GetOpt(opts, "--workers", "1"));
        var engine = GetOpt(opts, "--engine", "raw");
        if (ttReset is not ("fresh" or "shared"))
        {
            Console.Error.WriteLine("--tt-reset must be fresh|shared");
            Environment.Exit(2);
        }
        if (engine is not ("raw" or "session"))
        {
            Console.Error.WriteLine("--engine must be raw (direct Searcher, shared TT access) or session (ParallelSearcher single worker, direct TT access)");
            Environment.Exit(2);
        }

        if (depth <= 0 || depth > Constants.MaxSearchDepth || repeat <= 0 || workers <= 0)
            throw new ArgumentOutOfRangeException(nameof(opts), "Depth, repeat and workers must be positive and depth within the engine limit.");

        var positions = LoadBenchCorpus(corpusPath);

        var ttCount = checked((int)TranspositionTableExtensions.CalculateTranspositionTableSize(ttMb));
        var ttBytes = (nuint)sizeof(Transposition) * (nuint)ttCount;

        if (workers > 1)
        {
            RunSmpBench(opts, positions, depth, ttMb, ttBytes, ttCount, workers, repeat, ttReset, strictReset);
            return;
        }

        // Single-worker paths. "raw" preserves the historical direct-Searcher measurement
        // (shared TT access flag); "session" measures the production single-worker
        // ParallelSearcher route, which uses direct (unflagged) TT access. Checksums
        // between the two must match at equal TT bytes and entry count.
        var ttAccess = engine == "raw" ? "shared" : "direct";

        Transposition* tt = null;
        Searcher? searcher = null;
        ParallelSearcher? session = null;
        var gameState = GameState.InitialState();
        try
        {
            if (engine == "raw")
            {
                tt = Bench.AllocateTranspositions((nuint)ttCount);
                searcher = new Searcher(tt, ttCount); // construction failure still frees tt in finally
            }
            else
            {
                session = new ParallelSearcher(ttCount); // worker construction failure frees its table
            }

            var results = new List<object>();
            var totalNodes = 0L;
            var totalElapsedNs = 0L;
            var totalManagedAllocatedBytes = 0L;
            var totalGen0Collections = 0;
            var totalGen1Collections = 0;
            var totalGen2Collections = 0;
            var aggregateChecksum = 14695981039346656037UL;
            var perPositionChecksums = positions.ToDictionary(p => p.fen, _ => new List<ulong>());
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var workingSetBeforeBytes = process.WorkingSet64;
            var sampledPeakWorkingSetBytes = workingSetBeforeBytes;
            var managedHeapBeforeBytes = GC.GetTotalMemory(forceFullCollection: false);
            var gen0BeforeBench = GC.CollectionCount(0);
            var gen1BeforeBench = GC.CollectionCount(1);
            var gen2BeforeBench = GC.CollectionCount(2);

            for (var rep = 0; rep < repeat; rep++)
            {
                foreach (var (fen, tag) in positions)
                {
                    if (ttReset == "fresh")
                    {
                        if (tt != null)
                            NativeMemory.Clear(tt, ttBytes);
                        else if (session is not null)
                            NativeMemory.Clear(session.Transpositions, ttBytes);
                        else
                            throw new InvalidOperationException("No transposition table is available.");
                    }

                    gameState.ResetToFen(fen);

                    (List<uint> pv, int depthSearched, int score, long nodes) r;
                    var allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();
                    var gen0BeforeSearch = GC.CollectionCount(0);
                    var gen1BeforeSearch = GC.CollectionCount(1);
                    var gen2BeforeSearch = GC.CollectionCount(2);
                    var start = Stopwatch.GetTimestamp();
                    if (engine == "raw")
                    {
                        var activeSearcher = searcher ?? throw new InvalidOperationException("The raw searcher was not initialized.");
                        activeSearcher.Reset(gameState);
                        if (strictReset)
                        {
                            // Model a new game for each independent fixture. Per-search Reset
                            // deliberately retains correction history within the same game.
                            NativeMemory.Clear(activeSearcher.PawnCorrHist, (nuint)Searcher.TableElementsSize * sizeof(int));
                            NativeMemory.Clear(activeSearcher.WhiteMaterialCorrHist, (nuint)Searcher.TableElementsSize * sizeof(int));
                            NativeMemory.Clear(activeSearcher.BlackMaterialCorrHist, (nuint)Searcher.TableElementsSize * sizeof(int));
                        }
                        var (pv, depthSearched, score, nodes) = activeSearcher.Search(gameState, activeSearcher.Stop,
                            depthLimit: depth, threadId: -1);
                        r = (pv, depthSearched, score, nodes);
                    }
                    else
                    {
                        var activeSession = session ?? throw new InvalidOperationException("The search session was not initialized.");
                        if (strictReset)
                            activeSession.NewGame();
                        var (pv, depthSearched, score, nodes, _) = activeSession.DepthBoundSearch(gameState, depth);
                        r = (pv, depthSearched, score, nodes);
                    }
                    var elapsedNs = (Stopwatch.GetTimestamp() - start) * 1_000_000_000 / Stopwatch.Frequency;
                    var managedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore;
                    var gen0Collections = GC.CollectionCount(0) - gen0BeforeSearch;
                    var gen1Collections = GC.CollectionCount(1) - gen1BeforeSearch;
                    var gen2Collections = GC.CollectionCount(2) - gen2BeforeSearch;
                    process.Refresh();
                    sampledPeakWorkingSetBytes = Math.Max(sampledPeakWorkingSetBytes, process.WorkingSet64);

                    var bestMove = r.pv.Count > 0 ? r.pv[0].ToUciMoveName() : "(none)";
                    // Full PV in UCI notation. Built outside the timed region; additive field.
                    var pvUci = string.Join(' ', r.pv.Select(m => m.ToUciMoveName()));
                    var checksum = Fnv1a($"{fen}|{r.nodes}|{r.score}|{r.depthSearched}|{bestMove}");
                    perPositionChecksums[fen].Add(checksum);
                    aggregateChecksum ^= checksum;
                    aggregateChecksum *= 1099511628211;

                    totalNodes += r.nodes;
                    totalElapsedNs += elapsedNs;
                    totalManagedAllocatedBytes += managedAllocatedBytes;
                    totalGen0Collections += gen0Collections;
                    totalGen1Collections += gen1Collections;
                    totalGen2Collections += gen2Collections;

                    results.Add(new
                    {
                        rep,
                        fen,
                        tag,
                        nodes = r.nodes,
                        elapsedNs,
                        nps = elapsedNs > 0 ? (long)((double)r.nodes * 1_000_000_000.0 / elapsedNs) : 0L,
                        managedAllocatedBytes,
                        managedBytesPerNode = r.nodes > 0 ? (double)managedAllocatedBytes / r.nodes : 0,
                        gen0Collections,
                        gen1Collections,
                        gen2Collections,
                        score = r.score,
                        depthSearched = r.depthSearched,
                        bestMove,
                        pvLength = r.pv.Count,
                        pv = pvUci,
                        checksum = $"X{checksum:X16}",
                    });
                }
            }

            var deterministic = perPositionChecksums.All(kv => kv.Value.Distinct().Count() == 1);
            process.Refresh();

            WriteJson(new
            {
                header = BuildHeader(opts, [Sha256(corpusPath)]),
                configuration = new
                {
                    mode = "bench",
                    depth,
                    ttMb,
                    ttActualBytes = (long)ttBytes,
                    ttReset,
                    strictReset,
                    repeat,
                    workers = 1,
                    engine,
                    ttAccess,
                    timedRegion = "worker-reset-and-search; includes correction reset when strict",
                    corpus = Path.GetFileName(corpusPath),
                },
                positions = results,
                totals = new
                {
                    nodes = totalNodes,
                    elapsedNs = totalElapsedNs,
                    nps = totalElapsedNs > 0 ? (long)((double)totalNodes * 1_000_000_000.0 / totalElapsedNs) : 0L,
                    aggregateChecksum = $"X{aggregateChecksum:X16}",
                    deterministicAcrossRepeats = deterministic,
                    managedAllocatedBytes = totalManagedAllocatedBytes,
                    managedBytesPerNode = totalNodes > 0 ? (double)totalManagedAllocatedBytes / totalNodes : 0,
                    gen0CollectionsDuringSearch = totalGen0Collections,
                    gen1CollectionsDuringSearch = totalGen1Collections,
                    gen2CollectionsDuringSearch = totalGen2Collections,
                },
                memory = new
                {
                    samplingScope = "single-worker search calls; managed allocations on the calling thread",
                    workingSetBeforeBytes,
                    workingSetAfterBytes = process.WorkingSet64,
                    sampledPeakWorkingSetBytes,
                    managedHeapBeforeBytes,
                    managedHeapAfterBytes = GC.GetTotalMemory(forceFullCollection: false),
                    gen0CollectionsBefore = gen0BeforeBench,
                    gen0CollectionsAfter = GC.CollectionCount(0),
                    gen1CollectionsBefore = gen1BeforeBench,
                    gen1CollectionsAfter = GC.CollectionCount(1),
                    gen2CollectionsBefore = gen2BeforeBench,
                    gen2CollectionsAfter = GC.CollectionCount(2),
                },
            }, opts);
        }
        finally
        {
            searcher?.Dispose();
            session?.Dispose();
            gameState.Dispose();
            if (tt != null)
                MemoryHelpers.FreeAligned(tt);
        }
    }

    // Multi-worker throughput comparison for the TT publication protocol. SMP search
    // is inherently non-deterministic, so per-position checksums are not meaningful here; this
    // measures aggregate nodes/nps at equal TT bytes and equal entry count.
    private static unsafe void RunSmpBench(Dictionary<string, string> opts, List<(string fen, string tag)> positions,
        int depth, int ttMb, nuint ttBytes, int ttCount, int workers, int repeat, string ttReset, bool strictReset)
    {
        var results = new List<object>();
        var totalNodes = 0L;
        var totalElapsedNs = 0L;

        for (var rep = 0; rep < repeat; rep++)
        {
            var ps = new ParallelSearcher(ttCount);
            try
            {
                // SetThreads failure must not orphan the owned table.
                ps.SetThreads(workers);
                foreach (var (fen, tag) in positions)
                {
                    if (ttReset == "fresh")
                        NativeMemory.Clear(ps.Transpositions, ttBytes);

                    using var gameState = GameState.InitialState();
                    gameState.ResetToFen(fen);
                    if (strictReset)
                        ps.NewGame();

                    var start = Stopwatch.GetTimestamp();
                    var (_, depthSearched, score, nodes, _) = ps.DepthBoundSearch(gameState, depth);
                    var elapsedNs = (Stopwatch.GetTimestamp() - start) * 1_000_000_000 / Stopwatch.Frequency;

                    totalNodes += nodes;
                    totalElapsedNs += elapsedNs;
                    results.Add(new
                    {
                        rep,
                        fen,
                        tag,
                        nodes,
                        elapsedNs,
                        nps = elapsedNs > 0 ? (long)((double)nodes * 1_000_000_000.0 / elapsedNs) : 0L,
                        score,
                        depthSearched,
                    });
                }
            }
            finally
            {
                ps.Dispose();
            }
        }

        var effectiveWorkers = Math.Clamp(workers, 1, OperatingSystem.IsBrowser() ? 1 : Environment.ProcessorCount);
        WriteJson(new
        {
            header = BuildHeader(opts, [Sha256(ResolveCorpus(opts, "--corpus", "fixtures/bench-corpus-v1.txt"))]),
            configuration = new
            {
                mode = "bench-smp",
                depth,
                ttMb,
                ttActualBytes = (long)ttBytes,
                ttReset,
                strictReset,
                repeat,
                requestedWorkers = workers,
                workers = effectiveWorkers,
                engine = "session",
                ttAccess = effectiveWorkers == 1 ? "direct" : "shared",
                timedRegion = "worker-reset-and-search; correction reset excluded",
                corpus = Path.GetFileName(ResolveCorpus(opts, "--corpus", "fixtures/bench-corpus-v1.txt")),
            },
            positions = results,
            totals = new
            {
                nodes = totalNodes,
                elapsedNs = totalElapsedNs,
                nps = totalElapsedNs > 0 ? (long)((double)totalNodes * 1_000_000_000.0 / totalElapsedNs) : 0L,
                deterministicAcrossRepeats = false,
            },
        }, opts);
    }

    private static int RunPerft(Dictionary<string, string> opts)
    {
        var corpusPath = ResolveCorpus(opts, "--corpus", "fixtures/perft-corpus-v1.txt");
        var fixtures = File.ReadAllLines(corpusPath)
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
            .Select(l => l.Split('|'))
            .Select(p => (fen: p[0].Trim(), depth: int.Parse(p[1]), expected: ulong.Parse(p[2])))
            .ToList();

        if (fixtures.Count == 0)
        {
            Console.Error.WriteLine($"perft corpus contains no fixtures: {corpusPath}");
            return 2;
        }

        var duplicates = fixtures.GroupBy(f => (f.fen, f.depth)).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.fen} depth {g.Key.depth}").ToList();
        if (duplicates.Count > 0)
        {
            Console.Error.WriteLine($"perft corpus contains duplicate fixtures: {string.Join(", ", duplicates.Take(5))}");
            return 2;
        }

        if (fixtures.Any(f => f.depth < 1))
        {
            Console.Error.WriteLine("perft root-divide fixtures require depth >= 1");
            return 2;
        }

        var results = new List<object>();
        var allPass = true;

        foreach (var (fen, depth, expected) in fixtures)
        {
            var board = BoardStateExtensions.CreateBoardFromFen(fen);
            var start = Stopwatch.GetTimestamp();
            // Silent: per-move console progress must not contaminate the timed routine.
            var leaf = board.PerftRootSequential(depth, reportProgress: false);
            var elapsedNs = (Stopwatch.GetTimestamp() - start) * 1_000_000_000 / Stopwatch.Frequency;

            ulong sum = 0;
            foreach (var (nodes, _) in leaf)
            {
                sum += nodes;
            }

            var pass = sum == expected;
            allPass &= pass;

            results.Add(new
            {
                fen,
                depth,
                expected,
                actual = sum,
                pass,
                elapsedNs,
                nps = elapsedNs > 0 ? (long)((double)sum * 1_000_000_000.0 / elapsedNs) : 0L,
            });
        }

        WriteJson(new
        {
            header = BuildHeader(opts, [Sha256(corpusPath)]),
            configuration = new { mode = "perft", fixtures = fixtures.Count },
            positions = results,
            totals = new { allPass },
        }, opts);
        return allPass && fixtures.Count > 0 ? 0 : 1;
    }

    private static ulong Fnv1a(string s)
    {
        var h = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            h ^= b;
            h *= 1099511628211;
        }

        return h;
    }
}
