using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sapling.Engine.Transpositions;

public static class TranspositionTableExtensions
{
    public const int NoHashEntry = 25_000;

    public const int
        PositiveCheckmateDetectionLimit =
            27_000;

    public const int
        NegativeCheckmateDetectionLimit =
            -27_000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int RecalculateMateScores(int score, int ply)
    {
        return score
               + score switch
               {
                   > PositiveCheckmateDetectionLimit => -ply,
                   < NegativeCheckmateDetectionLimit => ply,
                   _ => 0
               };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe (int Evaluation, uint BestMove, TranspositionTableFlag NodeType) Get(Transposition* tt,
        ulong ttMask, ulong hash, int depth, int ply, int alpha, int beta)
    {
        ref var entry = ref tt[hash & ttMask];

        if (hash != entry.FullHash)
        {
            return (NoHashEntry, default, default);
        }

        var eval = NoHashEntry;

        if (entry.Depth < depth)
        {
            return (eval, entry.Move, entry.Flag);
        }

        // We want to translate the checkmate position relative to the saved node to our root position from which we're searching
        // If the recorded score is a checkmate in 3 and we are at depth 5, we want to read checkmate in 8
        var score = RecalculateMateScores(entry.Evaluation, ply);

        eval = entry.Flag switch
        {
            TranspositionTableFlag.Exact => score,
            TranspositionTableFlag.Alpha when score <= alpha => alpha,
            TranspositionTableFlag.Beta when score >= beta => beta,
            _ => NoHashEntry
        };

        return (eval, entry.Move, entry.Flag);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Set(Transposition* tt, uint ttMask, ulong hash, byte depth, int ply,
        int eval, TranspositionTableFlag nodeType, uint move = default)
    {
        ref var entry = ref tt[hash & ttMask];

        var shouldReplace =
            entry.FullHash == 0 // No actual entry
            || hash != entry.FullHash // Different key: collision
            || nodeType == TranspositionTableFlag.Exact // Entering PV data
            || depth >= entry.Depth; // Higher depth

        if (!shouldReplace)
        {
            return;
        }

        entry.FullHash = hash;
        entry.Evaluation = RecalculateMateScores(eval, -ply);
        entry.Depth = depth;
        entry.Flag = nodeType;
        entry.Move = move != 0 ? move : entry.Move; //Don't clear TT move if no best move is provided: keep old one
    }

    public static uint CalculateTranspositionTableSize(int sizeInMb)
    {
        var transpositionCount = ((ulong)sizeInMb * 1024ul * 1024ul) / 18;
        if (!BitOperations.IsPow2(transpositionCount))
        {
            transpositionCount = BitOperations.RoundUpToPowerOf2(transpositionCount) >> 1;
        }

        if (transpositionCount > int.MaxValue)
        {
            throw new ArgumentException($"Transposition table too large");
        }

        return (uint)transpositionCount;
    }

    public static int CalculateSizeInMb(uint transpositionCount)
    {
        // If transpositionCount is less than 2, the original function would have shifted it
        // to a power of 2 and then shifted it back down by >> 1, so adjust it.
        if (transpositionCount < 2 || !BitOperations.IsPow2(transpositionCount))
        {
            throw new ArgumentException("Invalid transposition count, must be a power of 2.");
        }

        // Reverse the potential shift caused by rounding up in the original function
        ulong adjustedTranspositionCount = transpositionCount << 1;

        // Calculate the size in MB
        int sizeInMb = (int)((adjustedTranspositionCount * 18) / (1024 * 1024));

        // Check if the original function would have produced the same transposition count
        // for this sizeInMb, if not, decrement the size until it matches.
        while (CalculateTranspositionTableSize(sizeInMb) != transpositionCount)
        {
            sizeInMb--;
            if (sizeInMb < 0)
            {
                throw new ArgumentException("Could not invert the function, invalid input.");
            }
        }

        return sizeInMb;
    }

}