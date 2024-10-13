using System.Diagnostics;
using System.Runtime.CompilerServices;
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
    public BinaryWriter Writer;
    public DataGeneratorStats(BinaryWriter writer)
    {
        Writer = writer;
        InitialBoard.ResetToFen(Constants.InitialState);
    }

    public unsafe void Output(Span<BulletFormat> positions, int positionCount, byte result)
    {
        // Calculate the total size needed to store all BulletFormat structures
        var totalSize = positionCount * BulletFormat.Size;

        // Allocate a buffer on the stack to hold all positions
        Span<byte> buffer = stackalloc byte[totalSize];

        // Copy all BulletFormat structures to the buffer
        fixed (void* bufferPtr = buffer, positionsPtr = positions)
        {
            Unsafe.CopyBlock(bufferPtr, positionsPtr, (uint)totalSize);
        }

        lock (OutputLock)
        {
            Positions += (ulong)positionCount;
            switch (result)
            {
                case 2:
                    Wins++;
                    break;
                case 1:
                    Looses++;
                    break;
                default:
                    Draws++;
                    break;
            }

            // Write the entire buffer to the writer
            Writer.Write(buffer);
        }
    }


    public void Output()
    {
        lock (OutputLock)
        {
            Writer.Flush();
        }

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

    public bool Cancelled = false;
    public void Start()
    {
        Cancelled = false;
#if AVX512
            Console.WriteLine("Using Avx-512");
#else
        Console.WriteLine("Using Avx-256");
#endif

        using var fileStream = new FileStream("./out.bullet", FileMode.Append, FileAccess.Write);
        using var writer = new BinaryWriter(fileStream);
        var stats = new DataGeneratorStats(writer);

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
        _ =>
        {
            RunWorker(stats);
        });

        stats.Output();
        Console.WriteLine("Datagen finished");
    }

    static bool IsAdjudicatedDraw(GameState gameState, int drawScoreCount)
    {
        return gameState.Board.TurnCount >= 60 && gameState.Board.HalfMoveClock >= 20 && drawScoreCount >= 4;
    }

    private unsafe void RunWorker(DataGeneratorStats stats)
    {
        var ttSize = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(256);
        var transpositionTable = MemoryHelpers.Allocate<Transposition>(ttSize);
        var searcher = new Searcher(transpositionTable, ttSize);

        BoardStateData boardState = default;
        stats.InitialBoard.CloneTo(ref boardState);

        Span<bool> turns = stackalloc bool[MaxTurnCount];
        Span<BulletFormat> dataGenPositions = stackalloc BulletFormat[MaxTurnCount];
        var gameState = new GameState(boardState);
        var initialLegalMoves = gameState.LegalMoves.ToArray();
        while (!Cancelled)
        {
            try
            {
                var (result, positions) = RunGame(gameState, searcher, turns, dataGenPositions);
                for (var index = 0; index < positions; index++)
                {
                    dataGenPositions[index].UpdateWdl(turns[index], result);
                }

                stats.Output(dataGenPositions, positions, result);
                if (Is960)
                {
                    gameState.ResetToFen(Chess960.Fens[Random.Shared.Next(0, 960)]);
                }
                else
                {
                    gameState.ResetTo(ref stats.InitialBoard, initialLegalMoves);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

    }

    public (byte result, int positions) RunGame(GameState gameState, Searcher searcher, Span<bool> turns, Span<BulletFormat> dataGenPositions)
    {
        var randomMoveCount = 0;
        var positions = 0;
        var adjudicationCounter = 0;
        var score = 0;
        var drawScoreCount = 0;
        while (!gameState.GameOver() && gameState.Board.TurnCount < MaxTurnCount && !IsAdjudicatedDraw(gameState, drawScoreCount))
        {
            uint move;
            if (randomMoveCount <= RandomMoves)
            {
                move = gameState.LegalMoves[Random.Shared.Next(0, gameState.LegalMoves.Count)];
                randomMoveCount++;
            }
            else
            {
                var (pv, _, s, _) = searcher.Search(gameState, nodeLimit: 6500, depthLimit: 60, writeInfo: false);
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

                if (move.IsQuiet() && !gameState.Board.InCheck)
                {
                    turns[positions] = gameState.Board.WhiteToMove;
                    dataGenPositions[positions] = BulletFormat.Pack(ref gameState.Board, (short)score, 0);
                    positions++;
                }
            }

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

        byte result;
        if (adjudicationCounter > 4)
        {
            if (gameState.Board.WhiteToMove)
            {
                result = score > 0 ? (byte)1 : (byte)2;
            }
            else
            {
                result = score > 0 ? (byte)2 : (byte)1;
            }
        }
        else if (IsAdjudicatedDraw(gameState, drawScoreCount))
        {
            result = 0;
        }
        else
        {
            result = gameState.WinDrawLoose();
        }

        return (result, positions);
    }
}