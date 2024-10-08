using System.Diagnostics;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling;

public class UciEngine
{
    public int TranspositionSize = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(256);

    private static readonly string[] PositionLabels = { "position", "fen", "moves" };
    private static readonly string[] GoLabels = { "go", "movetime", "wtime", "btime", "winc", "binc", "movestogo" };
    private readonly StreamWriter _logWriter;

    private ParallelSearcher _parallelSearcher;
    private DateTime _dt = DateTime.Now;
    private GameState _gameState = GameState.InitialState();
    private readonly DataGenerator _dataGen = new();

    private bool _ponderEnabled = false;
    private int _threadCount = 1;
    private readonly string _version;
    public UciEngine(StreamWriter logWriter)
    {
        var version = typeof(Program).Assembly.GetName().Version;
        _version = $"{version.Major}-{version.Minor}-{version.Build}";

        _logWriter = logWriter;
        _parallelSearcher = new ParallelSearcher(TranspositionSize);
        _parallelSearcher.SetThreads(1);
    }

    private void SetOption(string[] tokens)
    {
        if (tokens[1] != "name")
        {
            return;
        }

        if (SpsaTuner.HasParameter(tokens[2].ToLower()))
        {
            SpsaTuner.SetParameterValue(tokens);
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
                    _threadCount = searchThreads;
                    _parallelSearcher.SetThreads(searchThreads);
                    LogToFile($"[Debug] Set Threads '{searchThreads}'");
                }

                break;
            case "hash":
                {
                    if (tokens[3] == "value" && int.TryParse(tokens[4], out var transpositionSize))
                    {
                        TranspositionSize = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(transpositionSize);
                        _parallelSearcher = new(TranspositionSize);
                        _parallelSearcher.SetThreads(_threadCount);
                        LogToFile($"[Debug] Set Transposition Size '{TranspositionSize}'");
                    }
                    break;
                }
        }
    }

    public void ReceiveCommand(string message)
    {
        LogToFile($"Request -> '{message}'");

        var loweredMessage = message.Trim().ToLower();
        var tokens = message.Split(' ');
        var messageType = tokens[0];

        switch (messageType)
        {
            case "uci":
                Respond($"id name Sapling {_version}");
                Respond("id author Tim Jones");
                Respond($"option name Threads type spin default {_threadCount} min 1 max 1024");
                Respond($"option name Ponder type check default {_ponderEnabled.ToString().ToLower()}");
                Respond($"option name Hash type spin default {TranspositionTableExtensions.CalculateSizeInMb((uint)TranspositionSize)} min 32 max 2046");
#if OpenBench
                foreach (var spsaParameters in SpsaTuner.TuningParameters.Values)
                {
                    Respond($"option name {spsaParameters.Name} type spin default {spsaParameters.DefaultValue} min {spsaParameters.MinValue} max {spsaParameters.MaxValue}");

                }
#endif
                Respond("uciok");
                break;
            case "isready":
                Respond("readyok");
                break;
            case "ucinewgame":
                _gameState.Reset();
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
        // FEN
        if (message.ToLower().Contains("startpos"))
        {
            _gameState.Reset();
        }
        else if (message.ToLower().Contains("fen"))
        {
            var customFen = TryGetLabelledValue(message, "fen", PositionLabels);
            _gameState.ResetToFen(customFen.ToString());
        }
        else
        {
            Console.WriteLine("Invalid position command (expected 'startpos' or 'fen')");
        }

        // Moves
        var allMoves = TryGetLabelledValue(message, "moves", PositionLabels);
        if (allMoves.IsEmpty || allMoves.IsWhiteSpace())
        {
            return;
        }

        var moveList = SplitSpan(allMoves, ' ');
        foreach (var move in moveList)
        {
            var mov = _gameState.LegalMoves.FirstOrDefault(m => m.ToUciMoveName() == move);
            if (mov == default)
            {
                var moves = string.Join(",", _gameState.LegalMoves.Select(m => m.ToUciMoveName()));
                LogToFileForce("ERRROR!");
                LogToFileForce("Couldn't apply move: " + move +
                               $" for {(_gameState.Board.WhiteToMove ? "white" : "black")}");
                LogToFileForce("Valid moves: " + moves);

                LogToFileForce($"Error applying move: '{mov.ToMoveString()}'");
                break;
            }

            _gameState.Apply(mov);
        }
    }

    private unsafe void ProcessGoCommand(string message)
    {
        var messageSegments = message.Split(' ');
        var loweredMessage = message.ToLower();
        if (loweredMessage.StartsWith("go perft"))
        {
            var depth = int.Parse(messageSegments[2]);
            var stopWatch = Stopwatch.StartNew();
            ulong totalNodeCount = 0;
            foreach (var (nodeCount, move) in _gameState.Board.PerftRootParallel(depth))
            {
                totalNodeCount += nodeCount;
            }

            Respond(
                $"{totalNodeCount} in {stopWatch.ElapsedMilliseconds}, {(long)totalNodeCount / stopWatch.ElapsedMilliseconds} KNPS");

            return;
        }

        if (loweredMessage.StartsWith("go see"))
        {
            var seeMove = messageSegments[2];
            var mov = _gameState.LegalMoves.FirstOrDefault(m => m.ToUciMoveName() == seeMove);
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

        if (loweredMessage.StartsWith("go depth"))
        {
            var depth = int.Parse(messageSegments[2]);
            OnMoveChosen(_parallelSearcher.DepthBoundSearch(_gameState, depth));
            return;
        }

        var thinkTime = 0;

        if (message.Contains("movetime"))
        {
            thinkTime = TryGetLabelledValueInt(message, "movetime", GoLabels);
        }
        else
        {
            var timeRemainingWhiteMs = TryGetLabelledValueInt(message, "wtime", GoLabels);
            var timeRemainingBlackMs = TryGetLabelledValueInt(message, "btime", GoLabels);
            var incrementWhiteMs = TryGetLabelledValueInt(message, "winc", GoLabels);
            var incrementBlackMs = TryGetLabelledValueInt(message, "binc", GoLabels);

            thinkTime = ChooseThinkTime(timeRemainingWhiteMs, timeRemainingBlackMs, incrementWhiteMs,
                incrementBlackMs, 30);
        }

        if (thinkTime is <= 0 or >= 30_000)
        {
            // Limit think time to 30 seconds
            thinkTime = 30_000;
        }

        OnMoveChosen(_parallelSearcher.TimeBoundSearch(_gameState, thinkTime));
    }

    private void OnMoveChosen(
        (List<uint> move, int depthSearched, int score, int nodes, TimeSpan duration) result)
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

    public void Info((List<uint> move, int depthSearched, int score, int nodes, TimeSpan duration) result)
    {
        var nps = (int)(result.nodes / result.duration.TotalSeconds);
        Respond(
            $"info depth {result.depthSearched} score {ScoreToString(result.score)} nodes {result.nodes} nps {nps} time {(int)result.duration.TotalMilliseconds} pv {(string.Join(" ", result.move.Select(m => m.ToUciMoveName())))}");
    }

    public int ChooseThinkTime(int timeRemainingWhiteMs, int timeRemainingBlackMs, int incrementWhiteMs,
        int incrementBlackMs, int movesToGo)
    {
        var myTimeRemainingMs = _gameState.Board.WhiteToMove ? timeRemainingWhiteMs : timeRemainingBlackMs;
        var myIncrementMs = _gameState.Board.WhiteToMove ? incrementWhiteMs : incrementBlackMs;
        // Get a fraction of remaining time to use for current move
        var thinkTimeMs = myTimeRemainingMs / (float)movesToGo;

        // Add increment
        if (myTimeRemainingMs > myIncrementMs * 2)
        {
            thinkTimeMs += myIncrementMs * 0.8f;
        }

        var minThinkTime = Math.Min(50, myTimeRemainingMs * 0.25);
        return (int)Math.Ceiling(Math.Max(minThinkTime, thinkTimeMs));
    }

    public static List<string> SplitSpan(ReadOnlySpan<char> span, char delimiter)
    {
        // Create a list to hold the resulting spans
        var result = new List<string>();

        // Variables to track the start of the current span and the end of the last span
        var start = 0;
        int index;

        while ((index = span[start..].IndexOf(delimiter)) != -1)
        {
            // Create a span for the current segment and add it to the result
            var segment = span.Slice(start, index);
            result.Add(segment.ToString());
            // Move start to the character after the delimiter
            start += index + 1;
        }

        // Add the final segment
        var finalSegment = span.Slice(start);
        if (!finalSegment.IsEmpty)
        {
            result.Add(finalSegment.ToString());
        }

        // Convert the list to an array
        return result;
    }

    private void Respond(string message)
    {
        Console.WriteLine(message);
        LogToFile("Response <- " + message);
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

    private static int TryGetLabelledValueInt(ReadOnlySpan<char> text, ReadOnlySpan<char> label, string[] allLabels,
        int defaultValue = 0)
    {
        // Get the labelled value as a span
        var valueSpan = TryGetLabelledValue(text, label, allLabels, defaultValue.ToString());

        // Split the span to extract the first part, try to parse it as an int
        var spaceIndex = valueSpan.IndexOf(' ');
        var valuePart = spaceIndex >= 0 ? valueSpan[..spaceIndex] : valueSpan;

        return int.TryParse(valuePart, out var result) ? result : defaultValue;
    }

    private static ReadOnlySpan<char> TryGetLabelledValue(ReadOnlySpan<char> text, ReadOnlySpan<char> label,
        string[] allLabels, ReadOnlySpan<char> defaultValue = default)
    {
        text = text.Trim();

        // Check if the label is present in the text
        var labelIndex = text.IndexOf(label, StringComparison.Ordinal);
        if (labelIndex == -1)
        {
            return defaultValue;
        }

        // Calculate the start index of the value
        var valueStart = labelIndex + label.Length;
        var valueEnd = text.Length;

        // Determine the end of the value by looking for the next label
        foreach (var labelId in allLabels)
        {
            if (labelId.AsSpan().SequenceEqual(label))
            {
                continue;
            }

            var labelIdIndex = text[valueStart..].IndexOf(labelId, StringComparison.Ordinal);
            if (labelIdIndex == -1)
            {
                continue;
            }

            valueEnd = valueStart + labelIdIndex;
            break;
        }

        // Return the trimmed span of the value
        return text.Slice(valueStart, valueEnd - valueStart).Trim();
    }

    private void LogToFile(string text)
    {
        if (!UciOptions.IsDebug)
        {
            return;
        }

        var delta = DateTime.Now - _dt;
        _dt = DateTime.Now;
        _logWriter.WriteLine($"{delta.TotalMilliseconds}ms {text}");
    }

    private void LogToFileForce(string text)
    {
        var delta = DateTime.Now - _dt;
        _dt = DateTime.Now;
        _logWriter.WriteLine($"{delta.TotalMilliseconds}ms {text}");
    }
}