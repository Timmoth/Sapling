using System.Diagnostics;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling;

public class UciEngine : IDisposable
{
    private readonly object _commandGate = new();
    private bool _disposed;

    public long TranspositionSize = (long)TranspositionTableExtensions.CalculateTranspositionTableSize(256);

    public static int MaxHashMb()
    {
        var availableMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));
        return Math.Clamp(availableMb / 2, 32, 1_000_000);
    }

    private readonly StreamWriter _logWriter;

    private ParallelSearcher _parallelSearcher;
    private long _logTimestamp = Stopwatch.GetTimestamp();
    private GameState _gameState = GameState.InitialState();
    private readonly DataGenerator _dataGen = new();

    private bool _ponderEnabled = false;
    private volatile bool _aborted;
    private int _threadCount = 1;
    private readonly Random _humanRng = new();
    private readonly string _version;
    public UciEngine(StreamWriter logWriter)
    {
        var version = typeof(Program).Assembly.GetName().Version;
        _version = $"{version.Major}.{version.Minor}.{version.Build}";

        _logWriter = logWriter;
        _parallelSearcher = new ParallelSearcher(TranspositionSize);
        _parallelSearcher.SetThreads(1);
    }

    private void SetOption(string[] tokens)
    {
        if (tokens.Length < 5)
        {
            ReportProtocolError("setoption expects 'name <option> value <value>'");
            return;
        }

        if (tokens[1] != "name")
        {
            return;
        }

        if (SpsaTuner.HasParameter(tokens[2].ToLowerInvariant()))
        {
            if (!SpsaTuner.SetParameterValue(tokens))
            {
                ReportProtocolError($"Invalid value for SPSA parameter {tokens[2]}");
            }
        }

        switch (tokens[2].ToLower())
        {
            case "ponder":
                {
                    if (tokens[3] == "value" && bool.TryParse(tokens[4], out var value))
                    {
                        _ponderEnabled = value;
                    }
                    break;
                }

            case "threads":
                if (tokens[3] == "value" && int.TryParse(tokens[4], out var searchThreads))
                {
                    _threadCount = Math.Clamp(searchThreads, 1, Environment.ProcessorCount);
                    _parallelSearcher.SetThreads(_threadCount);
                    LogToFile($"[Debug] Set Threads '{searchThreads}'");
                }

                break;
            case "hash":
                {
                    if (tokens[3] != "value" || !int.TryParse(tokens[4], out var transpositionSize))
                    {
                        ReportProtocolError("setoption name Hash value <mb> expects a number");
                        break;
                    }

                    if (transpositionSize < 32)
                    {
                        ReportProtocolError($"Hash must be at least 32 MB, got {transpositionSize}");
                        break;
                    }

                    var maxHash = MaxHashMb();
                    if (transpositionSize > maxHash)
                    {
                        ReportProtocolError($"Hash {transpositionSize} MB exceeds available memory; clamping to {maxHash} MB");
                        transpositionSize = maxHash;
                    }

                    var size = TranspositionTableExtensions.CalculateTranspositionTableSize(transpositionSize);
                    var replacement = new ParallelSearcher(size);
                    try { replacement.SetThreads(_threadCount); }
                    catch { replacement.Dispose(); throw; }
                    var previous = Interlocked.Exchange(ref _parallelSearcher, replacement);
                    TranspositionSize = size;
                    previous.Dispose();
                    LogToFile($"[Debug] Set Transposition Size '{TranspositionSize}'");
                    break;
                }

            case "humanelo":
                if (tokens[3] == "value" && int.TryParse(tokens[4], out var humanElo))
                {
                    HumanPlay.SetElo(Math.Clamp(humanElo, 0, 3000));
                    LogToFile($"[Debug] Set HumanElo '{humanElo}'");
                }

                break;
        }
    }

    public void Dispose()
    {
        _dataGen.Cancelled = true;
        // Dispose marks the session closed before joining. A flag-only Stop here can
        // be lost when a command has acquired its gate but has not started searching.
        Volatile.Read(ref _parallelSearcher).Dispose();
        lock (_commandGate)
        {
            if (_disposed) return;
            _disposed = true;
            _parallelSearcher.Dispose();
            _gameState.Dispose();
        }
    }

    /// <summary>
    /// Stops any running search and refuses later ones. Used by the command reader on quit/EOF so
    /// queued commands can still be drained without a pending 'go' outracing the shutdown.
    /// </summary>
    public void Abort()
    {
        _aborted = true;
        _dataGen.Cancelled = true;
        Volatile.Read(ref _parallelSearcher).Abort();
    }

    public void ReceiveCommand(string message)
    {
        if (message.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            Volatile.Read(ref _parallelSearcher).Stop();
            _dataGen.Cancelled = true;
            return;
        }
        if (message.Trim().Equals("ponderhit", StringComparison.OrdinalIgnoreCase))
        {
            if (_ponderEnabled)
                Volatile.Read(ref _parallelSearcher).Stop();
            return;
        }
        lock (_commandGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                ReceiveCommandCore(message);
            }
            catch (Exception ex)
            {
                // A malformed command is a protocol error, never a reason to end the session.
                ReportProtocolError($"ignoring \"{message}\": {ex.Message}");
            }
        }
    }

    private void ReceiveCommandCore(string message)
    {
        LogToFile($"Request -> '{message}'");

        var loweredMessage = message.Trim().ToLower();
        var tokens = Tokenize(message);
        if (tokens.Length == 0)
        {
            return;
        }

        var messageType = tokens[0];

        switch (messageType)
        {
            case "uci":
                Respond($"id name Sapling {_version}");
                Respond("id author Tim Jones");
                Respond($"option name Threads type spin default {_threadCount} min 1 max 1024");
                Respond($"option name Ponder type check default {_ponderEnabled.ToString().ToLower()}");
                Respond($"option name Hash type spin default {TranspositionTableExtensions.CalculateSizeInMb((uint)TranspositionSize)} min 32 max {MaxHashMb()}");
                Respond("option name HumanElo type spin default 0 min 0 max 3000");
                // UCI_Chess960 is deliberately not advertised: castling is only verified for
                // standard chess, so advertising it would let GUIs start unsupported games.
#if OpenBench
                foreach (var spsaParameters in SpsaTuner.TuningParameters.Values)
                {
                    if (spsaParameters.Type == "int")
                    {
                        Respond($"option name {spsaParameters.Name} type spin default {spsaParameters.DefaultValue} min {spsaParameters.MinValue} max {spsaParameters.MaxValue}");
                    }
                    else
                    {
                        Respond($"option name {spsaParameters.Name} type string default {spsaParameters.DefaultValue}");
                    }

                }
#endif
                Respond("uciok");
                break;
            case "isready":
                Respond("readyok");
                break;
            case "ucinewgame":
                _gameState.Reset();
                _parallelSearcher.NewGame();
                break;
            case "position":
                ProcessPositionCommand(message);
                break;
            case "go":
                ProcessGoCommand(loweredMessage);
                break;
            case "stop":
                _parallelSearcher.Stop();
                _dataGen.Cancelled = true;
                break;
            case "setoption":
                SetOption(tokens);
                break;
            case "ponderhit":
                break;
            case "d":
                Console.WriteLine(_gameState.CreateDiagram());
                break;
            case "datagen":
                _dataGen.Start();
                break;
            case "bench":
                Bench.Run();
                break;
            case "rank":
                // Offline calibration helper: print the root moves ranked best-first with their
                // fixed-depth scores, for the human-data mining pipeline. Not a UCI keyword.
                if (tokens.Length < 2 || !int.TryParse(tokens[1], out var rankDepth) || rankDepth < 1)
                {
                    ReportProtocolError("rank requires a positive depth");
                    break;
                }

                foreach (var (mv, sc, _) in _parallelSearcher.RankRootMoves(_gameState, Math.Min(rankDepth, Constants.MaxSearchDepth)))
                    Console.WriteLine($"rankmove {mv.ToUciMoveName()} {sc}");
                Console.WriteLine("rankdone");
                break;
            case "version":
                Console.WriteLine(_version);
                break;
            case "tune":
                SpsaTuner.ProcessUCIOptions();
                foreach (var formattedParameter in SpsaTuner.PrintSPSAParams())
                {
                    Console.WriteLine(formattedParameter);
                }
                break;
            default:
                LogToFileForce($"Unrecognized command: {messageType}");
                break;
        }
    }

    private void ProcessPositionCommand(string message)
    {
        var tokens = Tokenize(message);
        if (tokens.Length < 2)
        {
            ReportProtocolError("position requires 'startpos' or 'fen'");
            return;
        }

        var movesIndex = -1;
        for (var i = 2; i < tokens.Length; i++)
        {
            if (tokens[i].Equals("moves", StringComparison.OrdinalIgnoreCase))
            {
                movesIndex = i;
                break;
            }
        }

        var fenEnd = movesIndex == -1 ? tokens.Length : movesIndex;

        if (tokens[1].Equals("startpos", StringComparison.OrdinalIgnoreCase))
        {
            _gameState.Reset();
        }
        else if (tokens[1].Equals("fen", StringComparison.OrdinalIgnoreCase))
        {
            var customFen = string.Join(" ", tokens[2..fenEnd]);
            if (FenValidator.Validate(customFen) is { } fenError)
            {
                // Reject the command; the previous position and the session both stay alive.
                ReportProtocolError($"invalid FEN ({fenError}); position unchanged");
                return;
            }

            _gameState.ResetToFen(customFen);
        }
        else
        {
            ReportProtocolError("position expects 'startpos' or 'fen'");
            return;
        }

        // Moves
        if (movesIndex == -1 || movesIndex == tokens.Length - 1)
        {
            return;
        }

        for (var i = movesIndex + 1; i < tokens.Length; i++)
        {
            var move = tokens[i];
            var mov = _gameState.LegalMoves.FirstOrDefault(m => m.ToUciMoveName() == move);
            if (mov == default)
            {
                var moves = string.Join(",", _gameState.LegalMoves.Select(m => m.ToUciMoveName()));
                LogToFileForce("Couldn't apply move: " + move +
                               $" for {(_gameState.Board.WhiteToMove ? "white" : "black")}");
                LogToFileForce("Valid moves: " + moves);
                ReportProtocolError($"illegal move '{move}' in position command; remaining moves ignored");
                return;
            }

            try
            {
                _gameState.Apply(mov);
            }
            catch (InvalidOperationException ex)
            {
                ReportProtocolError($"cannot apply '{move}': {ex.Message}");
                return;
            }
        }
    }

    private unsafe void ProcessGoCommand(string message)
    {
        var tokens = Tokenize(message);
        if (tokens.Length >= 2 && tokens[1].Equals("perft", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3 || !int.TryParse(tokens[2], out var perftDepth) || perftDepth < 1)
            {
                ReportProtocolError("go perft requires a positive depth");
                return;
            }

            ulong totalNodeCount = 0;
            foreach (var (nodeCount, move) in _gameState.Board.PerftRootParallel(perftDepth))
            {
                totalNodeCount += nodeCount;
            }

            // Standard perft total; fast depth-1 runs may take zero milliseconds.
            Respond($"Nodes searched: {totalNodeCount}");

            return;
        }

        if (tokens.Length >= 2 && tokens[1].Equals("see", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3)
            {
                ReportProtocolError("go see requires a move");
                return;
            }

            var seeMove = tokens[2];
            var mov = _gameState.LegalMoves.FirstOrDefault(m => m.ToUciMoveName() == seeMove);
            if (mov == default)
            {
                ReportProtocolError($"go see: '{seeMove}' is not a legal move");
                return;
            }

            var occupancyBitBoards = stackalloc ulong[8]
            {
                _gameState.Board.Occupancy[Constants.WhitePieces],
                _gameState.Board.Occupancy[Constants.BlackPieces],
                _gameState.Board.Occupancy[Constants.BlackPawn] | _gameState.Board.Occupancy[Constants.WhitePawn],
                _gameState.Board.Occupancy[Constants.BlackKnight] | _gameState.Board.Occupancy[Constants.WhiteKnight],
                _gameState.Board.Occupancy[Constants.BlackBishop] | _gameState.Board.Occupancy[Constants.WhiteBishop],
                _gameState.Board.Occupancy[Constants.BlackRook] | _gameState.Board.Occupancy[Constants.WhiteRook],
                _gameState.Board.Occupancy[Constants.BlackQueen] | _gameState.Board.Occupancy[Constants.WhiteQueen],
                _gameState.Board.Occupancy[Constants.BlackKing] | _gameState.Board.Occupancy[Constants.WhiteKing]
            };

            var captures = stackalloc short[32];
            var seeScore = _gameState.Board.StaticExchangeEvaluation(occupancyBitBoards, captures, mov);
            Console.WriteLine(seeScore);
            return;
        }

        var depth = 0;
        var depthSpecified = false;
        var nodeLimit = 0;
        var nodesSpecified = false;
        var moveTime = int.MinValue;
        var hasWhiteTime = false;
        var hasBlackTime = false;
        var timeRemainingWhiteMs = 0;
        var timeRemainingBlackMs = 0;
        var incrementWhiteMs = 0;
        var incrementBlackMs = 0;
        var movesToGo = 0;

        for (var i = 1; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "depth":
                case "movetime":
                case "wtime":
                case "btime":
                case "winc":
                case "binc":
                case "movestogo":
                {
                    if (i + 1 >= tokens.Length || !int.TryParse(tokens[i + 1], out var value))
                    {
                        ReportProtocolError($"go {tokens[i]} requires a number");
                        return;
                    }

                    i++;
                    switch (tokens[i - 1])
                    {
                        case "depth":
                            depth = value;
                            depthSpecified = true;
                            break;
                        case "movetime":
                            moveTime = value;
                            break;
                        case "wtime":
                            hasWhiteTime = true;
                            timeRemainingWhiteMs = value;
                            break;
                        case "btime":
                            hasBlackTime = true;
                            timeRemainingBlackMs = value;
                            break;
                        case "winc":
                            incrementWhiteMs = value;
                            break;
                        case "binc":
                            incrementBlackMs = value;
                            break;
                        default:
                            movesToGo = value;
                            break;
                    }

                    break;
                }
                case "nodes":
                {
                    if (i + 1 >= tokens.Length || !int.TryParse(tokens[i + 1], out nodeLimit) || nodeLimit <= 0)
                    {
                        ReportProtocolError("go nodes requires a positive 32-bit count");
                        return;
                    }
                    nodesSpecified = true;
                    i++;
                    break;
                }
                case "searchmoves":
                    ReportProtocolError("go searchmoves is not supported");
                    return;
                case "infinite":
                case "ponder":
                    break;
                default:
                    LogToFileForce($"go parameter '{tokens[i]}' is not recognised and was ignored");
                    break;
            }
        }

        if (_aborted)
        {
            return;
        }

        if (depthSpecified)
        {
            if (depth < 1 || depth > Constants.MaxSearchDepth)
            {
                ReportProtocolError($"go depth must be between 1 and {Constants.MaxSearchDepth}");
                return;
            }

            OnMoveChosen(ApplyHumanLevel(_parallelSearcher.DepthBoundSearch(_gameState, depth)));
            return;
        }

        if (nodesSpecified)
        {
            OnMoveChosen(ApplyHumanLevel(_parallelSearcher.NodeBoundSearch(_gameState, nodeLimit)));
            return;
        }

        if (moveTime != int.MinValue && moveTime < 0)
        {
            ReportProtocolError("go movetime must be non-negative");
            return;
        }

        var whiteToMove = _gameState.Board.WhiteToMove;
        var timeBudget = TimeManagement.Calculate(
            whiteToMove ? timeRemainingWhiteMs : timeRemainingBlackMs,
            whiteToMove ? incrementWhiteMs : incrementBlackMs,
            movesToGo,
            _gameState.History.Count,
            whiteToMove ? hasWhiteTime : hasBlackTime,
            moveTime == int.MinValue ? -1 : moveTime,
            tokens.Contains("infinite", StringComparer.OrdinalIgnoreCase) ||
            (_ponderEnabled && tokens.Contains("ponder", StringComparer.OrdinalIgnoreCase)));

        OnMoveChosen(ApplyHumanLevel(_parallelSearcher.TimeBoundSearch(
            _gameState, timeBudget.SoftLimitMs, timeBudget.HardLimitMs)));
    }

    private static string[] Tokenize(string message) =>
        message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private const float MinSearchTimeMs = 10f;
    private const float OverheadBufferMs = 50f;
    private const float IncrementUsageFactorLong = 0.6f;
    private const float SoftLimitFactorLong = 0.75f;
    private const float HardLimitFactorLong = 0.90f;

    // Fallback simple allocator constants for short TCs
    private const float ShortTcThresholdMs = 120_000f; // under 2 minutes remaining
    private const float IncrementUsageFactorShort = 0.8f;
    private const float MinThinkRatioShort = 0.25f;
    private const float MinThinkAbsoluteShort = 50f;
    public static int CalculateThinkTime(
            int whiteTimeMs, int blackTimeMs,
            int whiteIncMs, int blackIncMs,
            int movesToGo, int plyCount,
            bool whiteToMove)
    {
        // 1) Select appropriate clock and subtract overhead
        float timeRemaining = (whiteToMove ? whiteTimeMs : blackTimeMs) - OverheadBufferMs;
        float inc = whiteToMove ? whiteIncMs : blackIncMs;
        timeRemaining = Math.Max(timeRemaining, MinSearchTimeMs);

        // 2) Estimate movesToGo if unknown
        if (movesToGo <= 0)
        {
            movesToGo = plyCount < 40 ? 30 : plyCount < 80 ? 20 : 10;
        }

        // 3) If we're in a short TC scenario, use simple formula
        if (timeRemaining < ShortTcThresholdMs)
        {
            // Basic per-move division
            float baseTime = timeRemaining / movesToGo;
            // Add a generous fraction of increment if affordably available
            if (timeRemaining > inc * 2)
                baseTime += inc * IncrementUsageFactorShort;

            // Guarantee a minimum think time
            float minThink = Math.Min(MinThinkAbsoluteShort, timeRemaining * MinThinkRatioShort);
            float thinkTime = Math.Max(minThink, baseTime);
            return (int)Math.Ceiling(thinkTime);
        }

        // 4) Otherwise, use the long-TC soft/hard limit allocator
        float baseAlloc = timeRemaining / (movesToGo + 2);
        float rawAlloc = baseAlloc + inc * IncrementUsageFactorLong;

        // Soft limit encourages iterative deepening
        float softLimit = rawAlloc * SoftLimitFactorLong;
        // Hard limit prevents flagging
        float hardLimit = timeRemaining * HardLimitFactorLong;

        // 5) Final think time is bounded by raw, soft, and hard limits
        float think = Math.Min(rawAlloc, softLimit);
        think = Math.Min(think, hardLimit);

        // 6) Enforce absolute minimum
        return (int)Math.Max(MinSearchTimeMs, Math.Ceiling(think));
    }
    /// <summary>
    /// Human-like difficulty override. Inert unless HumanPlay is enabled AND a rank distribution
    /// is loaded, so normal play is byte-for-byte unchanged. When active it ranks the root moves at
    /// a fixed depth and samples which rank to play; rank 0 keeps the engine's own best move.
    /// </summary>
    private (List<uint> move, int depthSearched, int score, long nodes, TimeSpan duration) ApplyHumanLevel(
        (List<uint> move, int depthSearched, int score, long nodes, TimeSpan duration) result)
    {
        if (!HumanPlay.Enabled || result.move.Count == 0)
            return result;

        var ranked = _parallelSearcher.RankRootMoves(_gameState, HumanPlay.RankDepth);
        if (ranked.Count == 0)
            return result;

        var scores = new int[ranked.Count];
        var feats = new int[ranked.Count];
        for (var i = 0; i < ranked.Count; i++)
        {
            scores[i] = ranked[i].Score;
            feats[i] = ranked[i].Feat;
        }
        var rank = HumanPlay.PickRank(_humanRng, scores, feats);
        if (rank <= 0)
            return result; // play the engine's own best move (keeps PV + ponder)

        if (rank >= ranked.Count)
            rank = ranked.Count - 1;

        var chosen = ranked[rank].Move;
        if (chosen == result.move[0])
            return result;

        return (new List<uint> { chosen }, result.depthSearched, ranked[rank].Score, result.nodes, result.duration);
    }

    private void OnMoveChosen(
        (List<uint> move, int depthSearched, int score, long nodes, TimeSpan duration) result)
    {
        if (result == default)
        {
            return;
        }

        Info(result);

        if (result.move.Count == 0)
        {
            if (_gameState.LegalMoves.Any())
            {
                LogToFileForce("No moves generated, picked randomly.");
                Respond($"bestmove {_gameState.LegalMoves.FirstOrDefault().ToUciMoveName()}");
            }
            else
            {
                LogToFileForce("No legal moves available.");
                Respond($"bestmove (none)");
            }
            return;
        }

        if (_ponderEnabled && result.move.Count > 1)
        {
            Respond($"bestmove {result.move[0].ToUciMoveName()} ponder {result.move[1].ToUciMoveName()}");
        }
        else
        {
            Respond($"bestmove {result.move[0].ToUciMoveName()}");
        }
    }

    public void Info((List<uint> move, int depthSearched, int score, long nodes, TimeSpan duration) result)
    {
        var nps = (int)(result.nodes / result.duration.TotalSeconds);
        Respond(
            $"info depth {result.depthSearched} score {ScoreToString(result.score)} nodes {result.nodes} nps {nps} time {(int)result.duration.TotalMilliseconds} pv {(string.Join(" ", result.move.Select(m => m.ToUciMoveName())))}");
    }

    private void Respond(string message)
    {
        Console.WriteLine(message);
        LogToFile("Response <- " + message);
    }

    /// <summary>
    /// Reports a rejected command as an info string line: GUIs ignore it, a human reading the
    /// session sees why the command was dropped, and the command loop keeps running.
    /// </summary>
    private void ReportProtocolError(string message)
    {
        Console.WriteLine($"info string error: {message}");
        LogToFileForce("Error -> " + message);
    }

    private static string ScoreToString(int score)
    {
        if (MoveScoring.IsMateScore(score))
        {
            var sign = Math.Sign(score);
            var moves = MoveScoring.GetMateDistance(score);
            return $"mate {sign * moves}";
        }

        return $"cp {score}";
    }

    private void LogToFile(string text)
    {
        if (!UciOptions.IsDebug)
        {
            return;
        }

        LogElapsed(text);
    }

    private void LogToFileForce(string text)
    {
        LogElapsed(text);
    }

    // Monotonic log deltas: wall-clock DateTime can jump (NTP/manual changes/sleep).
    private void LogElapsed(string text)
    {
        var now = Stopwatch.GetTimestamp();
        var delta = Stopwatch.GetElapsedTime(_logTimestamp, now);
        _logTimestamp = now;
        _logWriter.WriteLine($"{delta.TotalMilliseconds}ms {text}");
    }
}
