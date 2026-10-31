using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sapling.Engine.Transpositions;

public static class TranspositionTableExtensions
{
    public const int NoHashEntry = 25_000;

    public struct TtSnapshot
    {
        public bool Valid; // coherent snapshot (no write interleaved the read)
        public ulong FullHash;
        public byte Depth;
        public int Evaluation;
        public TranspositionTableFlag Flag;
        public uint Move;
    }

    // Seqlock over an atomic payload: the whole payload lives in Stamp, so a
    // reader that brackets the FullHash read with two identical even Stamp reads
    // has a consistent snapshot. Any generation mix fails the Stamp re-read or
    // the caller's FullHash check and becomes a miss; readers never spin.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TtSnapshot Probe(ref Transposition entry, bool shared = true)
    {
        var stamp = shared ? Volatile.Read(ref entry.Stamp) : entry.Stamp;
        if (shared && ((stamp >> 58) & 1UL) != 0)
            return default;

        var hash = shared ? Volatile.Read(ref entry.FullHash) : entry.FullHash;

        if (shared && Volatile.Read(ref entry.Stamp) != stamp)
            return default;

        return new TtSnapshot
        {
            Valid = true,
            FullHash = hash,
            Depth = (byte)(stamp >> 48),
            Evaluation = (short)(stamp >> 32),
            Flag = (TranspositionTableFlag)(byte)((stamp >> 56) & 3),
            Move = (uint)stamp,
        };
    }

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
    public static void Set(this ref Transposition entry, ulong hash, byte depth, int ply,
        int eval, TranspositionTableFlag nodeType, uint move = default, bool shared = true)
    {
        var curStamp = shared ? Volatile.Read(ref entry.Stamp) : entry.Stamp;
        var curHash = shared ? Volatile.Read(ref entry.FullHash) : entry.FullHash;

        // Replacement policy on the snapshot (same rule as before: store when the
        // slot is empty, holds a different hash, is Exact, or is shallower).
        if (curHash != 0 && hash == curHash && nodeType != TranspositionTableFlag.Exact
            && depth < (byte)(curStamp >> 48))
            return;

        // A zero-move store may keep the incumbent move only when it is the SAME
        // position. Across a hash collision the incumbent move belongs to a
        // different position and must not be privileged.
        var newMove = move != 0 ? move : (curHash == hash ? (uint)curStamp : 0u);
        var version = (int)((curStamp >> 58) & 0x3F);
        var newStamp = Transposition.PackStamp(newMove, RecalculateMateScores(eval, -ply), depth, nodeType,
            shared ? (version + 2) & 0x3F : version);

        if (shared)
        {
            if ((version & 1) != 0 ||
                Interlocked.CompareExchange(ref entry.Stamp, curStamp + (1UL << 58), curStamp) != curStamp)
                return;

            Volatile.Write(ref entry.FullHash, hash);
            Volatile.Write(ref entry.Stamp, newStamp);
        }
        else
        {
            entry.FullHash = hash;
            entry.Stamp = newStamp;
        }
    }

    public static unsafe long CalculateTranspositionTableSize(long sizeInMb)
    {
        if (sizeInMb <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeInMb), "Size must be positive.");

        const long EightGB = 8L * 1024;
        const long TwoGB = 2L * 1024;

        // Entry COUNT is computed with the historical 20-byte divisor so a given
        // Hash setting yields the identical power-of-two count (and therefore the
        // identical indexing mask and collision pattern) as before the 16-byte
        // entry layout — this keeps search trees bit-identical across the change.
        // Allocation uses the real 16-byte stride, so the table uses ~20% less RAM.
        const long NominalEntryBytes = 20;

        if (sizeInMb > long.MaxValue / (1024 * 1024))
            throw new OverflowException("Requested size is too large.");

        ulong estimatedCount = (ulong)sizeInMb * 1024 * 1024 / NominalEntryBytes;

        if (estimatedCount == 0)
            throw new OverflowException("Requested size is too small to store even one transposition.");

        // Find powers of two above and below the estimated count
        ulong lower = BitOperations.IsPow2(estimatedCount)
            ? estimatedCount
            : BitOperations.RoundUpToPowerOf2(estimatedCount) >> 1;

        ulong upper = lower << 1;
        if (upper < lower)
            throw new OverflowException("Transposition table entry count overflows.");

        // Calculate size in MB for the rounded counts (nominal bytes: keeps the
        // round-up/round-down decision identical to the pre-16B layout).
        long lowerMb = (long)(lower * (ulong)NominalEntryBytes / (1024 * 1024));
        long upperMb = (long)(upper * (ulong)NominalEntryBytes / (1024 * 1024));

        ulong finalCount;

        if (sizeInMb <= EightGB)
        {
            // Always round up under 8GB
            finalCount = upper;
        }
        else if ((upperMb - sizeInMb) <= TwoGB)
        {
            // Round up if it's within 2GB
            finalCount = upper;
        }
        else
        {
            // Otherwise round down
            finalCount = lower;
        }

        // Return the selected power of two directly: rounding via integer MB corrupts
        // small table sizes and makes hash & (count - 1) an invalid indexing mask.

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

        // Report the bytes this table actually occupies. The size-to-count conversion retains
        // a historical 20-byte divisor for search-tree compatibility, but entries now have a
        // 16-byte stride. Reversing that conversion reports a larger Hash setting than the
        // allocated table and can cause UCI clients to write back an inflated default.
        var allocatedBytes = (ulong)transpositionCount * (ulong)sizeof(Transposition);
        return checked((int)(allocatedBytes / (1024 * 1024)));
    }
}
