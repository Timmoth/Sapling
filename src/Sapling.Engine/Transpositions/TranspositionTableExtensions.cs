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
        if (score > PositiveCheckmateDetectionLimit)
        {
            return score - ply; // Positive checkmate, reduce score by ply
        }
        if (score < NegativeCheckmateDetectionLimit)
        {
            return score + ply; // Negative checkmate, increase score by ply
        }
        return score; // No change
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
    public static void Set(this ref Transposition entry, ulong hash, byte depth, int ply,
        int eval, TranspositionTableFlag nodeType, uint move = default)
    {
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

    public static unsafe long CalculateTranspositionTableSize(long sizeInMb)
    {
        if (sizeInMb <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeInMb), "Size must be positive.");

        const long EightGB = 8L * 1024;
        const long TwoGB = 2L * 1024;

        ulong estimatedCount = (ulong)sizeInMb * 1024 * 1024 / (ulong)sizeof(Transposition);

        if (estimatedCount == 0)
            throw new OverflowException("Requested size is too small to store even one transposition.");

        // Find powers of two above and below the estimated count
        ulong lower = BitOperations.IsPow2(estimatedCount)
            ? estimatedCount
            : BitOperations.RoundUpToPowerOf2(estimatedCount) >> 1;

        ulong upper = lower << 1;

        // Calculate size in MB for the rounded counts
        long lowerMb = (long)(lower * (ulong)sizeof(Transposition) / (1024 * 1024));
        long upperMb = (long)(upper * (ulong)sizeof(Transposition) / (1024 * 1024));

        long chosenMb;

        if (sizeInMb <= EightGB)
        {
            // Always round up under 8GB
            chosenMb = upperMb;
        }
        else if ((upperMb - sizeInMb) <= TwoGB)
        {
            // Round up if it's within 2GB
            chosenMb = upperMb;
        }
        else
        {
            // Otherwise round down
            chosenMb = lowerMb;
        }

        ulong finalCount = (ulong)chosenMb * 1024 * 1024 / (ulong)sizeof(Transposition);

        if (finalCount == 0 || finalCount > long.MaxValue)
            throw new OverflowException("Final transposition table size is too large.");

        return (long)finalCount;
    }

    public static unsafe int CalculateSizeInMb(uint transpositionCount)
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
        var sizeInMb = (int)(adjustedTranspositionCount * (ulong)sizeof(Transposition) / (1024 * 1024));

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