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
    private bool _isPonderHit;
    private bool _isPondering;
    private bool _isReady = true;
    private (uint move, int depthSearched, int score, uint ponder, int nodes, TimeSpan duration) _result;
    private bool _ponderEnabled = false;
    private int _threadCount = 1;
    public UciEngine(StreamWriter logWriter)
    {
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
                    Respond("id name Sapling BETA");
                    Respond("id author Tim Jones");
                    Respond("id author Tim Jones");
                    Respond($"option name Threads type spin default {_threadCount} min 1 max 1024");
                    Respond($"option name Ponder type check default {_ponderEnabled.ToString().ToLower()}");
                    Respond($"option name Hash type spin default {TranspositionTableExtensions.CalculateSizeInMb((uint)TranspositionSize)}");
                    Respond("uciok");
                    break;
                case "isready":
                    if (_isReady)
                    {
                        Respond("readyok");
                    }
                    break;
                case "ucinewgame":
                    _gameState = GameState.InitialState();
                    _isReady = true;
                    break;
                case "position":
                    _isReady = false;
                    _result = default;
                    ProcessPositionCommand(message);
                    _isReady = true;
                    break;
                case "go":
                    _isReady = false;
                    ProcessGoCommand(loweredMessage);
                    _isReady = true;
                    break;
                case "stop":
                    if (_isPondering)
                    {
                        OnMoveChosen(_result);
                    }
                    else
                    {
                        _parallelSearcher.Stop();
                        _isPonderHit = true;
                    }

                    break;
                case "setoption":
                    SetOption(tokens);
                    break;
                case "ponderhit":
                    if (_isPondering)
                    {
                        OnMoveChosen(_result);
                    }
                    else
                    {
                        _isPonderHit = true;
                    }

                    break;
                case "d":
                    Console.WriteLine(_gameState.CreateDiagram());
                    break;
                case "datagen":
                    var dataGen = new DataGenerator();
                    dataGen.Start();
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
        (uint move, int depthSearched, int score, uint ponder, int nodes, TimeSpan duration) result)
    {
        Info(result);

        var bestMove = result.move.ToUciMoveName();

        if (_ponderEnabled && result.ponder != 0)
        {
            var ponderMove = result.ponder.ToUciMoveName();
            Respond($"bestmove {bestMove} ponder {ponderMove}");
        }
        else
        {
            Respond($"bestmove {bestMove}");
        }
    }

    public void Info((uint move, int depthSearched, int score, uint ponder, int nodes, TimeSpan duration) result)
    {
        var nps = (int)(result.nodes / result.duration.TotalSeconds);
        Respond(
            $"info depth {result.depthSearched} score {ScoreToString(result.score)} nodes {result.nodes} nps {nps} time {(int)result.duration.TotalMilliseconds} pv {result.move.ToUciMoveName()} {(result.ponder != 0 ? result.ponder.ToUciMoveName() : "")}");
    }

    private void ProcessGoCommand(string message)
    {
        var messageSegments = message.Split(' ');
        var loweredMessage = message.ToLower();
        if (loweredMessage.StartsWith("go perft"))
        {
            var depth = int.Parse(messageSegments[2]);
            var stopWatch = Stopwatch.StartNew();
            ulong totalNodeCount = 0;
            foreach (var (nodeCount, move) in _simpleSearcher.Board.PerftRootParallel(depth))
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
                _gameState.Board.WhitePieces, _gameState.Board.BlackPieces,
                _gameState.Board.BlackPawns | _gameState.Board.WhitePawns,
                _gameState.Board.BlackKnights | _gameState.Board.WhiteKnights,
                _gameState.Board.BlackBishops | _gameState.Board.WhiteBishops,
                _gameState.Board.BlackRooks | _gameState.Board.WhiteRooks,
                _gameState.Board.BlackQueens | _gameState.Board.WhiteQueens,
                _gameState.Board.BlackKings | _gameState.Board.WhiteKings
            };

            Span<short> captures = stackalloc short[32];
            var seeScore = _gameState.Board.StaticExchangeEvaluation(occupancyBitBoards, captures, mov);
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
                Respond(_gameState.Board.Evaluate().ToString());
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
        _isPondering = message.Contains("ponder");
        _isPonderHit = false;

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

        if (_isPondering && !_isPonderHit)
        {
            return;
        }

        _isPondering = false;
        OnMoveChosen(_result);
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

    private void ProcessPositionCommand(string message)
    {
        // FEN
        if (message.ToLower().Contains("startpos"))
        {
            //LogToFile($"startpos: {message}");
            _gameState.ResetTo(BoardStateExtensions.CreateBoardFromArray(Constants.InitialState));
            _simpleSearcher.Init(0, _gameState.Board);
        }
        else if (message.ToLower().Contains("fen"))
        {
            var customFen = TryGetLabelledValue(message, "fen", PositionLabels);
            var state = BoardStateExtensions.CreateBoardFromFen(customFen.ToString());
            _gameState = new GameState(state);
            _simpleSearcher.Init(0, _gameState.Board);
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
                      $" for {(_gameState.Board.WhiteToMove ? "white" : "black")}");
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