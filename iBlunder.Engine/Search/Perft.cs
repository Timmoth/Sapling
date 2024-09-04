using System.Collections.Concurrent;
using iBlunder.Engine.MoveGen;
using iBlunder.Engine.Pgn;

namespace iBlunder.Engine.Search;

public static class Perft
{
    private static ulong PerftInternal(this BoardState board, int depth)
    {
        Span<uint> moves = stackalloc uint[218];
        var moveCount = board.GeneratePseudoLegalMoves(moves, false);

        var originalHash = board.Hash;
        var oldEnpassant = board.EnPassantFile;
        var prevInCheck = board.InCheck;
        var prevCastleRights = board.CastleRights;
        var prevFiftyMoveCounter = board.FiftyMoveCounter;

        ulong nodeCount = 0;
        for (var index = 0; index < moveCount; index++)
        {
            var m = moves[index];

            if (board.PartialApply(m))
            {
                if (depth <= 1)
                {
                    // Leaf node, don't search any deeper
                    nodeCount++;
                }
                else
                {
                    board.UpdateCheckStatus();
                    nodeCount += board.PerftInternal(depth - 1);
                }
            }

            board.PartialUnApply(m, originalHash, oldEnpassant, prevInCheck, prevCastleRights, prevFiftyMoveCounter);
        }

        return nodeCount;
    }

    public static List<(ulong nodes, string move)> PerftRootSequential(this BoardState board, int depth)
    {
        var moves = new uint[218];
        var moveCount = board.GeneratePseudoLegalMoves(moves.AsSpan(), false);

        var rootMoves = new ConcurrentBag<(ulong nodes, string move)>();
        for (var i = 0; i < moveCount; i++)
        {
            var m = moves[i];

            var tempBoard = board.Clone();
            if (!tempBoard.PartialApply(m))
            {
                // Illegal move
                continue;
            }

            tempBoard.UpdateCheckStatus();
            var nodeCount = tempBoard.PerftInternal(depth - 1);

            Console.WriteLine(
                $"{PgnSplitter.ConvertPosition(m.GetFromSquare())}{PgnSplitter.ConvertPosition(m.GetToSquare())} {nodeCount}");
            rootMoves.Add((nodeCount,
                $"{PgnSplitter.ConvertPosition(m.GetFromSquare())}{PgnSplitter.ConvertPosition(m.GetToSquare())}"));
        }

        return rootMoves.ToList();
    }

    public static List<(ulong nodes, string move)> PerftRootParallel(this BoardState board, int depth)
    {
        var moves = new uint[218];
        var moveCount = board.GeneratePseudoLegalMoves(moves.AsSpan(), false);

        var rootMoves = new ConcurrentBag<(ulong nodes, string move)>();
        Parallel.For(0, moveCount, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, i =>
        {
            var m = moves[i];

            var tempBoard = board.Clone();
            if (!tempBoard.PartialApply(m))
            {
                // Illegal move
                return;
            }

            tempBoard.UpdateCheckStatus();
            var nodeCount = tempBoard.PerftInternal(depth - 1);

            Console.WriteLine(
                $"{PgnSplitter.ConvertPosition(m.GetFromSquare())}{PgnSplitter.ConvertPosition(m.GetToSquare())} {nodeCount}");
            rootMoves.Add((nodeCount,
                $"{PgnSplitter.ConvertPosition(m.GetFromSquare())}{PgnSplitter.ConvertPosition(m.GetToSquare())}"));
        });

        return rootMoves.ToList();
    }
}