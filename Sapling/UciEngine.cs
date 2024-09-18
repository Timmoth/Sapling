using System.Diagnostics;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling;

public class UciEngine
{
    public int TranspositionSize = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(256);
    public Transposition[] Transpositions;

    private static readonly string[] PositionLabels = { "position", "fen", "moves" };
    private static readonly string[] GoLabels = { "go", "movetime", "wtime", "btime", "winc", "binc", "movestogo" };
    private readonly StreamWriter _logWriter;

    private ParallelSearcher _parallelSearcher;
    private Searcher _simpleSearcher;
    private DateTime _dt = DateTime.Now;
    private GameState _gameState = GameState.InitialState();
    private (List<uint> move, int depthSearched, int score, int nodes, TimeSpan duration) _result;
    private bool _ponderEnabled = false;
    private int _threadCount = 1;
    private readonly string _version;
    public UciEngine(StreamWriter logWriter)
    {
        var version = typeof(Program).Assembly.GetName().Version;
        _version = $"{version.Major}-{version.Minor}-{version.Build}";

        _logWriter = logWriter;
        Transpositions = GC.AllocateArray<Transposition>(TranspositionSize, true);
        _parallelSearcher = new ParallelSearcher(Transpositions);
        _simpleSearcher = new Searcher(Transpositions);

        _parallelSearcher.SetThreads(1);
    }

    private void SetOption(string[] tokens)
    {
        if (tokens[1] != "name")
        {
            return;
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
                    Transpositions = GC.AllocateArray<Transposition>(TranspositionSize, true);
                    _simpleSearcher = new(Transpositions);
                    _parallelSearcher = new(Transpositions);
                    _parallelSearcher.SetThreads(_threadCount);
                    LogToFile($"[Debug] Set Transposition Size '{TranspositionSize}'");
                }
                break;
            }
            case "uci_opponent":
            {
                if (tokens[3] == "value")
                {
                    LogToFile($"Opponent: '{tokens[4]}'");
                }
                break;
            }
        }
    }

    public void ReceiveCommand(string message)
    {
        LogToFile($"Request -> '{message}'");

        try
        {
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
                    Respond("uciok");
                    break;
                case "isready":
                    Respond("readyok");
                    break;
                case "ucinewgame":
                    _gameState = GameState.InitialState();
                    break;
                case "position":
                    _result = default;
                    ProcessPositionCommand(message);
                    break;
                case "go":
                    Task.Run(() =>
                    {
                        ProcessGoCommand(loweredMessage);
                    });
                    break;
                case "stop":
                    _parallelSearcher.Stop();
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
                    var dataGen = new DataGenerator();
                    dataGen.Start();
                    break;              
                case "bench":
                    Bench.Run();
                    break;
                case "version":
                    Console.WriteLine(_version);
                    break;
                default:
                    LogToFile($"Unrecognized command: {messageType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            LogToFile($"'{message}' failed with exception");
            LogToFile("----------");
            LogToFile(ex.ToString());
            LogToFile("----------");
        }
    }

    private void OnMoveChosen(
        (List<uint> move, int depthSearched, int score, int nodes, TimeSpan duration) result)
    {
        if (result == default)
        {
            return;
        }

        Info(result);

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

    private unsafe void ProcessGoCommand(string message)
    {
        var messageSegments = message.Split(' ');
        var loweredMessage = message.ToLower();
        if (loweredMessage.StartsWith("go perft"))
        {
            var depth = int.Parse(messageSegments[2]);
            var stopWatch = Stopwatch.StartNew();
            ulong totalNodeCount = 0;
            foreach (var (nodeCount, move) in _gameState.Board.Data.PerftRootParallel(depth))
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
            var mov = _gameState.Moves.FirstOrDefault(m => m.ToUciMoveName() == seeMove);
            Span<ulong> occupancyBitBoards = stackalloc ulong[8]
            {
                _gameState.Board.Data.WhitePieces, _gameState.Board.Data.BlackPieces,
                _gameState.Board.Data.BlackPawns | _gameState.Board.Data.WhitePawns,
                _gameState.Board.Data.BlackKnights | _gameState.Board.Data.WhiteKnights,
                _gameState.Board.Data.BlackBishops | _gameState.Board.Data.WhiteBishops,
                _gameState.Board.Data.BlackRooks | _gameState.Board.Data.WhiteRooks,
                _gameState.Board.Data.BlackQueens | _gameState.Board.Data.WhiteQueens,
                _gameState.Board.Data.BlackKings | _gameState.Board.Data.WhiteKings
            };

            Span<short> captures = stackalloc short[32];
            var seeScore = _gameState.Board.Data.StaticExchangeEvaluation(occupancyBitBoards, captures, mov);
            Console.WriteLine(seeScore);
            return;
        }

        if (loweredMessage.StartsWith("go depth"))
        {
            var depth = int.Parse(messageSegments[2]);
            try
            {
                _result = _parallelSearcher.DepthBoundSearch(_gameState.Board, depth);
            }
            catch (Exception ex)
            {
                LogToFile($"'{message}' failed with exception");
                LogToFile("----------");
                LogToFile(ex.ToString());
                LogToFile("----------");
            }


            OnMoveChosen(_result);
            return;
        }

        if (loweredMessage.StartsWith("go eval"))
        {
            try
            {
                Respond(_gameState.Board.Data.Evaluate(_gameState.Board.WhiteAccumulator, _gameState.Board.BlackAccumulator).ToString());
            }
            catch (Exception ex)
            {
                LogToFile($"'{message}' failed with exception");
                LogToFile("----------");
                LogToFile(ex.ToString());
                LogToFile("----------");
            }
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

        try
        {
            _result = _parallelSearcher.TimeBoundSearch(_gameState.Board, thinkTime);
        }
        catch (Exception ex)
        {
            LogToFile($"'{message}' failed with exception");
            LogToFile("----------");
            LogToFile(ex.ToString());
            LogToFile("----------");
        }

        OnMoveChosen(_result);
    }

    public int ChooseThinkTime(int timeRemainingWhiteMs, int timeRemainingBlackMs, int incrementWhiteMs,
        int incrementBlackMs, int movesToGo)
    {
        var myTimeRemainingMs = _gameState.Board.Data.WhiteToMove ? timeRemainingWhiteMs : timeRemainingBlackMs;
        var myIncrementMs = _gameState.Board.Data.WhiteToMove ? incrementWhiteMs : incrementBlackMs;
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

    private void ProcessPositionCommand(string message)
    {
        // FEN
        if (message.ToLower().Contains("startpos"))
        {
            //LogToFile($"startpos: {message}");
            _gameState.ResetTo(BoardStateExtensions.CreateBoardFromArray(Constants.InitialState));
        }
        else if (message.ToLower().Contains("fen"))
        {
            var customFen = TryGetLabelledValue(message, "fen", PositionLabels);
            var state = BoardStateExtensions.CreateBoardFromFen(customFen.ToString());
            _gameState = new GameState(state);
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
            var mov = _gameState.Moves.FirstOrDefault(m => m.ToUciMoveName() == move);

            var isOk = _gameState.Apply(mov);

            if (isOk)
            {
                continue;
            }

            var moves = string.Join(",", _gameState.Moves.Select(m => m.ToUciMoveName()));
            LogToFile("ERRROR!");
            LogToFile("Couldn't apply move: " + move +
                      $" for {(_gameState.Board.Data.WhiteToMove ? "white" : "black")}");
            LogToFile("Valid moves: " + moves);

            LogToFile($"Error applying move: '{mov.ToMoveString()}'");
        }
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
        var delta = DateTime.Now - _dt;
        _dt = DateTime.Now;
        _logWriter.WriteLine($"{delta.TotalMilliseconds}ms {text}");
    }
}