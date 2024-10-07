using System.Diagnostics;
using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.DataGen;

public class DataGenerator
{
    public const int Iterations = 10000;
    public const int MaxGames = 20;
    public const int MaxTurnCount = 500;
    public static BoardStateData InitialBoard = default;
    private static readonly object OutputLock = new();
    public int Draws;
    public int Looses;
    public ulong Positions;
    public int Wins;

    public void Start()
    {
#if AVX512
            Console.WriteLine("Using Avx-512");
#else
        Console.WriteLine("Using Avx-256");
#endif

        using var fileStream = new FileStream("./out.bullet", FileMode.Append, FileAccess.Write);
        using var writer = new BinaryWriter(fileStream);

        var searchers = new List<ParallelSearcher>();
        var threads = Environment.ProcessorCount;
        var transpositionSize = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(256);

        for (var i = 0; i < threads; i++)
        {
            searchers.Add(new ParallelSearcher(transpositionSize));
        }

        InitialBoard.ResetToFen(Constants.InitialState);

        var stopwatch = Stopwatch.StartNew();

        Parallel.For(0, searchers.Count,
            j =>
            {
                for (var i = 0; i < Iterations; i++)
                {
                    RunIteration(writer, searchers[j]);

                    if (j != 0)
                    {
                        continue;
                    }

                    Console.WriteLine(
                        $"Wins: {Wins} Draws: {Draws} Loses: {Looses} Games: {Wins + Draws + Looses} duration: {stopwatch.Elapsed} Positions: {Positions.FormatBigNumber()} {(Positions / stopwatch.Elapsed.TotalSeconds).FormatBigNumber()}/s");

                    lock (OutputLock)
                    {
                        writer.Flush();
                        fileStream.Flush(true);
                    }
                }
            });
    }

    private unsafe void Output(BinaryWriter writer, Span<BulletFormat> positions, int positionCount, byte result)
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
            writer.Write(buffer);
        }
    }

    static bool IsAdjudicatedDraw(GameState gameState, int drawScoreCount)
    {
        return gameState.Board.TurnCount >= 60 && gameState.Board.HalfMoveClock >= 20 && drawScoreCount >= 4;
    }

    private void RunIteration(BinaryWriter writer, ParallelSearcher searcher)
    {
        try
        {

            BoardStateData boardState = default;
            InitialBoard.CloneTo(ref boardState);

            Span<bool> turns = stackalloc bool[MaxTurnCount];
            Span<BulletFormat> dataGenPositions = stackalloc BulletFormat[MaxTurnCount];
            var gameState = new GameState(boardState);
            var initialLegalMoves = gameState.LegalMoves.ToArray();

            for (var i = 0; i < MaxGames; i++)
            {
                var randomMoveCount = 0;
                var positions = 0;
                var adjudicationCounter = 0;
                var score = 0;
                var drawScoreCount = 0;
                while (!gameState.GameOver() && gameState.Board.TurnCount < MaxTurnCount && !IsAdjudicatedDraw(gameState, drawScoreCount))
                {
                    uint move = default;
                    if (randomMoveCount <= 8)
                    {
                        move = gameState.LegalMoves[Random.Shared.Next(0, gameState.LegalMoves.Count)];
                        randomMoveCount++;
                    }
                    else
                    {
                        var (pv, _, s, _, _) = searcher.NodeBoundSearch(gameState, 6500, 60);
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

                    if (Math.Abs(score) >= 2000)
                    {
                        if (++adjudicationCounter > 4 && score > 0)
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
                    result = boardState.WhiteToMove ? (byte)1 : (byte)2;
                }
                else if (IsAdjudicatedDraw(gameState, drawScoreCount))
                {
                    result = 0;
                }
                else
                {
                    result = gameState.WinDrawLoose();
                }

                for (var index = 0; index < positions; index++)
                {
                    dataGenPositions[index].UpdateWdl(turns[index], result);
                }

                Output(writer, dataGenPositions, positions, result);

                gameState.ResetTo(ref InitialBoard, initialLegalMoves);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}