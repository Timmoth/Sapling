using System.Diagnostics;
using System.Text;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.DataGen;

using System;

public class DataGeneratorStats
{
    public BoardStateData InitialBoard = default;
    public readonly object OutputLock = new();
    public int Draws;
    public int Looses;
    public ulong Positions;
    public int Wins;
    public Stopwatch Stopwatch = Stopwatch.StartNew();

    public DataGeneratorStats()
    {
        InitialBoard.ResetToFen(Constants.InitialState);
    }

    public void Update(int positionCount, byte result)
    {
        lock (OutputLock)
        {
            Positions += (ulong)positionCount;
            switch (result)
            {
                case Viriformat.WdlWhiteWin:
                    Wins++;
                    break;
                case Viriformat.WdlBlackWin:
                    Looses++;
                    break;
                default:
                    Draws++;
                    break;
            }
        }
    }

    public void Output()
    {
        var games = Wins + Draws + Looses;
        var elapsed = Stopwatch.Elapsed;
        Console.WriteLine(
            $"{DateTime.Now:t} duration: {(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2} Wins: {(Wins * 100 / (float)games).RoundToSignificantFigures(3)}% Draws: {(Draws * 100 / (float)games).RoundToSignificantFigures(3)}% Loses: {(Looses * 100 / (float)games).RoundToSignificantFigures(3)}% Games: {games.FormatBigNumber()} Positions: {Positions.FormatBigNumber()} {(Positions / (float)elapsed.TotalSeconds).RoundToSignificantFigures(3).FormatBigNumber()}/s");
    }
}

public class DataGenerator
{
    public const bool Is960 = true;
    public const int RandomMoves = 8;

    public const int MaxTurnCount = 500;

    // Node budget is sampled log-uniformly per game so the net sees a spread of
    // search strengths instead of a single fixed depth.
    public const int MinNodeBudget = 5_000;
    public const int MaxNodeBudget = 30_000;

    internal static bool IsRandomOpeningPly(int randomMoveCount) => randomMoveCount < RandomMoves;

    internal static BoardStateData CreateInitialBoard(bool useChess960, int chess960Index = 0)
    {
        if (!useChess960)
        {
            return Constants.InitialBoard;
        }

        if ((uint)chess960Index >= (uint)Chess960.Fens.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(chess960Index));
        }

        return BoardStateExtensions.CreateBoardFromFen(Chess960.Fens[chess960Index]);
    }

    internal static byte ToViriformatWdl(byte gameOutcome) => gameOutcome switch
    {
        0 => Viriformat.WdlDraw,
        1 => Viriformat.WdlBlackWin,
        2 => Viriformat.WdlWhiteWin,
        _ => throw new ArgumentOutOfRangeException(nameof(gameOutcome), gameOutcome,
            "Game outcome must be draw (0), black win (1), or white win (2).")
    };

    // Colour-swapped twin of a FEN: ranks flipped, piece colours swapped,
    // side-to-move swapped, and Shredder castling file-letters swapped case
    // (a white rook on file X on rank 1 becomes a black rook on file X on
    // rank 8). Playing each start and its mirror as a pair cancels any
    // colour or first-searched-move bias in the WDL labels.
    internal static string MirrorFen(string fen)
    {
        var parts = fen.Split(' ');

        var ranks = parts[0].Split('/');
        Array.Reverse(ranks);
        for (var i = 0; i < ranks.Length; i++)
        {
            var squares = ranks[i].ToCharArray();
            for (var j = 0; j < squares.Length; j++)
            {
                if (char.IsLetter(squares[j]))
                {
                    squares[j] = char.IsUpper(squares[j])
                        ? char.ToLowerInvariant(squares[j])
                        : char.ToUpperInvariant(squares[j]);
                }
            }

            ranks[i] = new string(squares);
        }

        var stm = parts[1] == "w" ? "b" : "w";

        var castling = parts[2];
        if (castling != "-")
        {
            var rights = new HashSet<char>();
            foreach (var c in castling)
            {
                rights.Add(char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
            }

            var sb = new StringBuilder();
            foreach (var c in rights.Where(char.IsUpper).OrderBy(c => c))
            {
                sb.Append(c);
            }

            foreach (var c in rights.Where(char.IsLower).OrderBy(c => c))
            {
                sb.Append(c);
            }

            castling = sb.ToString();
        }

        var ep = parts[3];
        if (ep != "-")
        {
            ep = $"{ep[0]}{(ep[1] == '3' ? '6' : '3')}";
        }

        return string.Join(" ", string.Join("/", ranks), stm, castling, ep, parts[4], parts[5]);
    }

    internal static byte GetAdjudicatedOutcome(bool whiteToMoveAfterMove, int score) =>
        whiteToMoveAfterMove
            ? score > 0 ? (byte)1 : (byte)2
            : score > 0 ? (byte)2 : (byte)1;

    public bool Cancelled = false;

    public void Start()
    {
        Cancelled = false;
#if AVX512
            Console.WriteLine("Using Avx-512");
#else
        Console.WriteLine("Using Avx-256");
#endif

        var stats = new DataGeneratorStats();

        using var timer = new Timer(_ =>
        {
            if (!Cancelled)
            {
                stats.Output();
            }
        }, null, 10_000, 60_000);

        var threads = Environment.ProcessorCount;
        Parallel.For(0, threads, new ParallelOptions()
        {
            MaxDegreeOfParallelism = threads
        },
        workerId =>
        {
            RunWorker(stats, workerId);
        });

        stats.Output();
        Console.WriteLine("Datagen finished");
    }

    static bool IsAdjudicatedDraw(GameState gameState, int drawScoreCount)
    {
        return gameState.Board.TurnCount >= 60 && gameState.Board.HalfMoveClock >= 20 && drawScoreCount >= 4;
    }

    static int SampleNodeBudget()
    {
        var t = Random.Shared.NextDouble();
        return (int)Math.Round(MinNodeBudget * Math.Pow((double)MaxNodeBudget / MinNodeBudget, t));
    }

    private unsafe void RunWorker(DataGeneratorStats stats, int workerId)
    {
        var ttMb = int.TryParse(Environment.GetEnvironmentVariable("SAPLING_DATAGEN_TT_MB"), out var configured) ? configured : 256;
        var ttSize = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(ttMb);
        var transpositionTable = MemoryHelpers.Allocate<Transposition>(ttSize);
        var searcher = new Searcher(transpositionTable, ttSize);

        using var fileStream = new FileStream($"./out-worker{workerId}.binpack", FileMode.Append, FileAccess.Write);
        using var writer = new BinaryWriter(fileStream);

        try
        {
        var boardState = Is960
            ? CreateInitialBoard(true, Random.Shared.Next(Chess960.Fens.Length))
            : stats.InitialBoard;

        Span<(uint move, short eval)> moves = stackalloc (uint move, short eval)[MaxTurnCount];
        Span<byte> gameScratch = stackalloc byte[Viriformat.MaxGameBytes];
        using var gameState = new GameState(boardState);
        var initialLegalMoves = gameState.LegalMoves.ToArray();
        while (!Cancelled)
        {
            try
            {
                var fen = Is960
                    ? Chess960.Fens[Random.Shared.Next(0, Chess960.Fens.Length)]
                    : Constants.InitialState;

                // Paired games: each start is played twice, once from each
                // colour, via its colour-swapped mirror. This cancels colour
                // and first-searched-move bias in the WDL labels.
                for (var pairIndex = 0; pairIndex < 2 && !Cancelled; pairIndex++)
                {
#pragma warning disable CS0162 // The generator is currently compiled in Chess960-only mode.
                    if (Is960)
                    {
                        gameState.ResetToFen(pairIndex == 0 ? fen : MirrorFen(fen));
                    }
                    else if (pairIndex == 0)
                    {
                        gameState.ResetTo(ref stats.InitialBoard, initialLegalMoves);
                    }
                    else
                    {
                        gameState.ResetToFen(MirrorFen(fen));
                    }
#pragma warning restore CS0162

                    var startBoard = gameState.Board;
                    var (result, moveCount) = RunGame(gameState, searcher, moves);
                    Viriformat.WriteGame(gameScratch, writer, ref startBoard, moves, moveCount, result);
                    // fsync per game: after each game the file is guaranteed to end on a
                    // complete game boundary, so power loss can never corrupt the stream.
                    writer.Flush();
                    fileStream.Flush(true);
                    stats.Update(moveCount, result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        }
        finally
        {
            writer.Flush();
            searcher.Dispose();
            MemoryHelpers.FreeAligned(transpositionTable);
        }
    }

    public (byte result, int moveCount) RunGame(GameState gameState, Searcher searcher, Span<(uint move, short eval)> moves)
    {
        var randomMoveCount = 0;
        var moveCount = 0;
        var adjudicationCounter = 0;
        var score = 0;
        var drawScoreCount = 0;
        var nodeBudget = SampleNodeBudget();
        while (!gameState.GameOver() && gameState.Board.TurnCount < MaxTurnCount && !IsAdjudicatedDraw(gameState, drawScoreCount))
        {
            uint move;
            if (IsRandomOpeningPly(randomMoveCount))
            {
                move = gameState.LegalMoves[Random.Shared.Next(0, gameState.LegalMoves.Count)];
                randomMoveCount++;
            }
            else
            {
                searcher.Reset(gameState);
                var (pv, _, s, _) = searcher.Search(gameState, searcher.Stop, nodeLimit: nodeBudget, depthLimit: 60);
                move = pv[0];
                score = s;

                if (score == 0)
                {
                    drawScoreCount++;
                }
                else
                {
                    drawScoreCount = 0;
                }
            }

            moves[moveCount++] = (move, (short)Math.Clamp(score, short.MinValue, short.MaxValue));

            gameState.Apply(move);

            if (Math.Abs(score) >= 2500)
            {
                if (++adjudicationCounter > 4)
                {
                    break;
                }
            }
            else
            {
                adjudicationCounter = 0;
            }
        }

        // WinDrawLoose/adjudication use: 0 = draw, 1 = black win, 2 = white win.
        byte outcome;
        if (adjudicationCounter > 4)
        {
            outcome = GetAdjudicatedOutcome(gameState.Board.WhiteToMove, score);
        }
        else if (IsAdjudicatedDraw(gameState, drawScoreCount))
        {
            outcome = 0;
        }
        else
        {
            outcome = gameState.WinDrawLoose();
        }

        var result = ToViriformatWdl(outcome);

        return (result, moveCount);
    }
}
