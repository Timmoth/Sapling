using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;

namespace Sapling.Engine.Search;

public static unsafe class NnueExtensions
{
#if AVX512
            const int VectorSize = 32; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#else
    public const int VectorSize = 16; // AVX2 operates on 16 shorts (256 bits = 16 x 16 bits)
#endif

    public const int AccumulatorSize = NnueWeights.Layer1Size / VectorSize;

    public const int L1ByteSize = sizeof(short) * NnueWeights.Layer1Size;
    public const int InputBucketWeightCount = NnueWeights.InputSize * AccumulatorSize;

    public const int BucketDivisor = (32 + NnueWeights.OutputBuckets - 1) / NnueWeights.OutputBuckets;

    private const int ColorStride = 64 * 6;
    private const int PieceStride = 64;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyQuiet(this ref AccumulatorState accumulatorState, int fromIndex, int toIndex)
    {
        // Precompute mirroring as integers
        var whiteMirrored = accumulatorState.WhiteMirrored ? 1 : 0;
        var blackMirrored = accumulatorState.BlackMirrored ? 1 : 0;

        // Precompute the bucket offsets
        var wBucketOffset = accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bBucketOffset = accumulatorState.BlackInputBucket * InputBucketWeightCount;

        // Update the accumulator state with calculated feature updates
        accumulatorState.WhiteSubFeatureUpdatesA = wBucketOffset + *(WhiteFeatureIndexes + fromIndex + whiteMirrored);
        accumulatorState.BlackSubFeatureUpdatesA = bBucketOffset + *(BlackFeatureIndexes + fromIndex + blackMirrored);
        accumulatorState.WhiteAddFeatureUpdatesA = wBucketOffset + *(WhiteFeatureIndexes + toIndex + whiteMirrored);
        accumulatorState.BlackAddFeatureUpdatesA = bBucketOffset + *(BlackFeatureIndexes + toIndex + blackMirrored);

        // Set the change type
        accumulatorState.ChangeType = AccumulatorChangeType.SubAdd;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyCapture(this ref AccumulatorState accumulatorState,
        int fromIndex, int toIndex,
        int capIndex)
    {
        // Precompute mirroring as integers
        var whiteMirrored = accumulatorState.WhiteMirrored ? 1 : 0;
        var blackMirrored = accumulatorState.BlackMirrored ? 1 : 0;

        // Precompute the bucket offsets
        var wBucketOffset = accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bBucketOffset = accumulatorState.BlackInputBucket * InputBucketWeightCount;

        // Update the accumulator state with calculated feature updates
        accumulatorState.WhiteSubFeatureUpdatesA = wBucketOffset + *(WhiteFeatureIndexes + fromIndex + whiteMirrored);
        accumulatorState.BlackSubFeatureUpdatesA = bBucketOffset + *(BlackFeatureIndexes + fromIndex + blackMirrored);
        accumulatorState.WhiteSubFeatureUpdatesB = wBucketOffset + *(WhiteFeatureIndexes + capIndex + whiteMirrored);
        accumulatorState.BlackSubFeatureUpdatesB = bBucketOffset + *(BlackFeatureIndexes + capIndex + blackMirrored);
        accumulatorState.WhiteAddFeatureUpdatesA = wBucketOffset + *(WhiteFeatureIndexes + toIndex + whiteMirrored);
        accumulatorState.BlackAddFeatureUpdatesA = bBucketOffset + *(BlackFeatureIndexes + toIndex + blackMirrored);

        // Set the change type
        accumulatorState.ChangeType = AccumulatorChangeType.SubSubAdd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyCastle(this ref AccumulatorState accumulatorState,
        int kingFromIndex, int kingToIndex, int rookFromIndex, int rookToIndex)
    {
        // Precompute mirroring as integers
        var whiteMirrored = accumulatorState.WhiteMirrored ? 1 : 0;
        var blackMirrored = accumulatorState.BlackMirrored ? 1 : 0;
        
        // Precompute bucket offsets
        var wBucketOffset = accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bBucketOffset = accumulatorState.BlackInputBucket * InputBucketWeightCount;

        // Update the accumulator state with calculated feature updates
        accumulatorState.WhiteSubFeatureUpdatesA = wBucketOffset + *(WhiteFeatureIndexes + kingFromIndex + whiteMirrored);
        accumulatorState.BlackSubFeatureUpdatesA = bBucketOffset + *(BlackFeatureIndexes + kingFromIndex + blackMirrored);
        accumulatorState.WhiteSubFeatureUpdatesB = wBucketOffset + *(WhiteFeatureIndexes + rookFromIndex + whiteMirrored);
        accumulatorState.BlackSubFeatureUpdatesB = bBucketOffset + *(BlackFeatureIndexes + rookFromIndex + blackMirrored);

        accumulatorState.WhiteAddFeatureUpdatesA = wBucketOffset + *(WhiteFeatureIndexes + kingToIndex + whiteMirrored);
        accumulatorState.BlackAddFeatureUpdatesA = bBucketOffset + *(BlackFeatureIndexes + kingToIndex + blackMirrored);
        accumulatorState.WhiteAddFeatureUpdatesB = wBucketOffset + *(WhiteFeatureIndexes + rookToIndex + whiteMirrored);
        accumulatorState.BlackAddFeatureUpdatesB = bBucketOffset + *(BlackFeatureIndexes + rookToIndex + blackMirrored);

        // Set the change type
        accumulatorState.ChangeType = AccumulatorChangeType.SubSubAddAdd;
    }


    public static readonly int* WhiteFeatureIndexes;
    public static readonly int* BlackFeatureIndexes;
    static NnueExtensions()
    {
        WhiteFeatureIndexes = MemoryHelpers.Allocate<int>(13 * 64 * 2);
        BlackFeatureIndexes = MemoryHelpers.Allocate<int>(13 * 64 * 2);
        for (byte i = 0; i <= Constants.WhiteKing; i++)
        {
            for (byte j = 0; j < 64; j++)
            {
                WhiteFeatureIndexes[i * 64 * 2 + j * 2 + 0] = WhiteFeatureIndices(0, i, j) * AccumulatorSize;
                WhiteFeatureIndexes[i * 64 * 2 + j * 2 + 1] = WhiteFeatureIndices(7, i, j) * AccumulatorSize;

                BlackFeatureIndexes[i * 64 * 2 + j * 2 + 0] = BlackFeatureIndices(0, i, j) * AccumulatorSize;
                BlackFeatureIndexes[i * 64 * 2 + j * 2 + 1] = BlackFeatureIndices(7, i, j) * AccumulatorSize; 
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WhiteFeatureIndices(int mirrored, int piece, byte square)
    {
        var white = piece & 1 ^ 1;
        var type = (piece >> 1) - white;

        return (white ^ 1) * ColorStride + type * PieceStride + square ^ mirrored;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BlackFeatureIndices(int mirrored, int piece, byte square)
    { 
        var white = piece & 1 ^ 1;

        var type = (piece >> 1) - white;
        return white * ColorStride + type * PieceStride + square ^ mirrored ^ 0x38;
    }
}