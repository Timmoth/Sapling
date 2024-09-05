using System.Diagnostics;
using System.Runtime.CompilerServices;
using iBlunder.Engine.MoveGen;
using iBlunder.Engine.Search;

namespace iBlunder.Engine.DataGen;

public class DataGenerator
{
    public const int Iterations = 10000;
    public const int MaxGames = 20;
    public const int MaxTurnCount = 100;
    public static readonly BoardState InitialBoard = BoardStateExtensions.CreateBoardFromArray(Constants.InitialState);
    private static readonly object OutputLock = new();
    public int Draws;
    public int Looses;
    public ulong Positions;
    public int Wins;

    public void Start()
    {
        using var fileStream = new FileStream("./out.bullet", FileMode.Append, FileAccess.Write);
        using var writer = new BinaryWriter(fileStream);

        var searchers = new List<ParallelSearcher>();
        var threads = Environment.ProcessorCount;
        for (var i = 0; i < threads; i++)
        {
            searchers.Add(new ParallelSearcher());
        }

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

    private void RunIteration(BinaryWriter writer, ParallelSearcher searcher)
    {
        try
        {
            var boardState = InitialBoard.Clone();

            Span<bool> turns = stackalloc bool[MaxTurnCount];
            Span<BulletFormat> dataGenPositions = stackalloc BulletFormat[MaxTurnCount];
            var gameState = new GameState(boardState);

            for (var i = 0; i < MaxGames; i++)
            {
                var randomMoveCount = 0;
                var positions = 0;
                var adjudicationCounter = 0;
                var score = 0;
                while (!gameState.GameOver() && gameState.Board.TurnCount < MaxTurnCount)
                {
                    uint move = default;
                    if (randomMoveCount <= 8)
                    {
                        move = gameState.Moves[Random.Shared.Next(0, gameState.Moves.Count)];
                        randomMoveCount++;
                    }
                    else
                    {
                        (move, _, score, _, _, _) = searcher.NodeBoundSearch(gameState.Board, 5000, 15);
                        if (move.IsQuiet() && !gameState.Board.InCheck)
                        {
                            turns[positions] = gameState.Board.WhiteToMove;
                            dataGenPositions[positions] = BulletFormat.Pack(gameState.Board, (short)score, 0);
                            positions++;
                        }
                    }

                    if (!gameState.Apply(move))
                    {
                        throw new Exception("Failed to apply move.");
                    }

                    if (Math.Abs(score) >= 2000)
                    {
                        if (++adjudicationCounter > 4 && score > 0)
                        {
                            break;
                        }
                    }
                    else
                        adjudicationCounter = 0;
                }

                byte result;
                if (adjudicationCounter > 4)
                {
                    result = boardState.WhiteToMove ? (byte)1 : (byte)2;
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

                gameState.ResetTo(InitialBoard);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}