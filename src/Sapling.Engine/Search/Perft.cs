using System.Collections.Concurrent;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Pgn;

namespace Sapling.Engine.Search;

public static class Perft
{
    private static unsafe ulong PerftInternal(this ref BoardStateData board, int depth)
    {
        var moves = stackalloc uint[218];
        var moveCount = board.GeneratePseudoLegalMoves(moves, false);
        BoardStateData copy = default;
        var whiteToMove = board.WhiteToMove;
        ulong nodeCount = 0;
        for (var index = 0; index < moveCount; index++)
        {
            var m = moves[index];
            board.CloneTo(ref copy);
            if (whiteToMove ? !copy.PartialApplyWhite(m) : !copy.PartialApplyBlack(m))
            {
                continue;
            }

            if (depth <= 1)
            {
                // Leaf node, don't search any deeper
                nodeCount++;
            }
            else
            {
                copy.UpdateCheckStatus();
                nodeCount += copy.PerftInternal(depth - 1);
            }
        }

        return nodeCount;
    }

    public static unsafe List<(ulong nodes, string move)> PerftRootSequential(this ref BoardStateData board, int depth)
    {
        var moves = stackalloc uint[218];
        var moveCount = board.GeneratePseudoLegalMoves(moves, false);
        BoardStateData copy = default;
        var whiteToMove = board.WhiteToMove;

        var rootMoves = new ConcurrentBag<(ulong nodes, string move)>();
        for (var i = 0; i < moveCount; i++)
        {
            var m = moves[i];
            board.CloneTo(ref copy);

            if (whiteToMove ? !copy.PartialApplyWhite(m) : !copy.PartialApplyBlack(m))
            {
                // Illegal move
                continue;
            }

            copy.UpdateCheckStatus();
            var nodeCount = copy.PerftInternal(depth - 1);

            Console.WriteLine(
                $"{m.GetFromSquare().ConvertPosition()}{m.GetToSquare().ConvertPosition()} {nodeCount}");
            rootMoves.Add((nodeCount,
                $"{m.GetFromSquare().ConvertPosition()}{m.GetToSquare().ConvertPosition()}"));
        }

        return rootMoves.ToList();
    }

    public static unsafe List<(ulong nodes, string move)> PerftRootParallel(this BoardStateData board, int depth)
    {
        var moves = stackalloc uint[218];
        var moveCount = board.GeneratePseudoLegalMoves(moves, false);
        var whiteToMove = board.WhiteToMove;

        var rootMoves = new ConcurrentBag<(ulong nodes, string move)>();
        Parallel.For(0, moveCount, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, i =>
        {
            BoardStateData copy = default;
            board.CloneTo(ref copy);

            var m = moves[i];

            if (whiteToMove ? !copy.PartialApplyWhite(m) : !copy.PartialApplyBlack(m))
            {
                // Illegal move
                return;
            }

            copy.UpdateCheckStatus();
            var nodeCount = copy.PerftInternal(depth - 1);

            Console.WriteLine(
                $"{m.GetFromSquare().ConvertPosition()}{m.GetToSquare().ConvertPosition()} {nodeCount}");
            rootMoves.Add((nodeCount,
                $"{m.GetFromSquare().ConvertPosition()}{m.GetToSquare().ConvertPosition()}"));
        });

        return rootMoves.ToList();
    }
}