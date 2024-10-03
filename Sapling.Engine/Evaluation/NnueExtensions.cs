using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    public static void ApplyQuiet(this ref AccumulatorState accumulatorState, byte fromPiece, byte fromSquare,
        byte toPiece, byte toSquare)
    {
        // Precompute mirroring as integers
        var whiteMirrored = accumulatorState.WhiteMirrored ? 1 : 0;
        var blackMirrored = accumulatorState.BlackMirrored ? 1 : 0;

        // Calculate indices once and reuse
        var fromIndex = (fromPiece * 128) + (fromSquare * 2);
        var toIndex = (toPiece * 128) + (toSquare * 2);

        // Precompute the bucket offsets
        var wBucketOffset = accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bBucketOffset = accumulatorState.BlackInputBucket * InputBucketWeightCount;

        // Directly calculate feature updates using precomputed indices
        var whiteFeatureFrom = WhiteFeatureIndexes[fromIndex + whiteMirrored];
        var blackFeatureFrom = BlackFeatureIndexes[fromIndex + blackMirrored];
        var whiteFeatureTo = WhiteFeatureIndexes[toIndex + whiteMirrored];
        var blackFeatureTo = BlackFeatureIndexes[toIndex + blackMirrored];

        // Update the accumulator state with calculated feature updates
        accumulatorState.WhiteSubFeatureUpdatesA = wBucketOffset + whiteFeatureFrom;
        accumulatorState.BlackSubFeatureUpdatesA = bBucketOffset + blackFeatureFrom;
        accumulatorState.WhiteAddFeatureUpdatesA = wBucketOffset + whiteFeatureTo;
        accumulatorState.BlackAddFeatureUpdatesA = bBucketOffset + blackFeatureTo;

        // Set the change type
        accumulatorState.ChangeType = AccumulatorChangeType.SubAdd;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyCapture(this ref AccumulatorState accumulatorState,
        byte fromPiece, byte fromSquare,
        byte toPiece, byte toSquare,
        byte capturedPiece, byte capturedSquare)
    {
        // Precompute mirroring as integers
        var whiteMirrored = accumulatorState.WhiteMirrored ? 1 : 0;
        var blackMirrored = accumulatorState.BlackMirrored ? 1 : 0;

        // Calculate indices once and reuse
        var fromIndex = (fromPiece * 128) + (fromSquare * 2);
        var toIndex = (toPiece * 128) + (toSquare * 2);
        var capIndex = (capturedPiece * 128) + (capturedSquare * 2);

        // Precompute the bucket offsets
        var wBucketOffset = accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bBucketOffset = accumulatorState.BlackInputBucket * InputBucketWeightCount;

        // Directly calculate feature updates using precomputed indices
        var whiteFeatureFrom = WhiteFeatureIndexes[fromIndex + whiteMirrored];
        var blackFeatureFrom = BlackFeatureIndexes[fromIndex + blackMirrored];
        var whiteFeatureCaptured = WhiteFeatureIndexes[capIndex + whiteMirrored];
        var blackFeatureCaptured = BlackFeatureIndexes[capIndex + blackMirrored];
        var whiteFeatureTo = WhiteFeatureIndexes[toIndex + whiteMirrored];
        var blackFeatureTo = BlackFeatureIndexes[toIndex + blackMirrored];

        // Update the accumulator state with calculated feature updates
        accumulatorState.WhiteSubFeatureUpdatesA = wBucketOffset + whiteFeatureFrom;
        accumulatorState.BlackSubFeatureUpdatesA = bBucketOffset + blackFeatureFrom;
        accumulatorState.WhiteSubFeatureUpdatesB = wBucketOffset + whiteFeatureCaptured;
        accumulatorState.BlackSubFeatureUpdatesB = bBucketOffset + blackFeatureCaptured;
        accumulatorState.WhiteAddFeatureUpdatesA = wBucketOffset + whiteFeatureTo;
        accumulatorState.BlackAddFeatureUpdatesA = bBucketOffset + blackFeatureTo;

        // Set the change type
        accumulatorState.ChangeType = AccumulatorChangeType.SubSubAdd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyCastle(this ref AccumulatorState accumulatorState,
    byte kingPiece, byte fromKingSquare, byte toKingSquare,
    byte rookPiece, byte fromRookSquare, byte toRookSquare)
    {
        // Precompute mirroring as integers
        var whiteMirrored = accumulatorState.WhiteMirrored ? 1 : 0;
        var blackMirrored = accumulatorState.BlackMirrored ? 1 : 0;

        // Calculate indices once and reuse
        var kingFromIndex = (kingPiece * 128) + (fromKingSquare * 2);
        var kingToIndex = (kingPiece * 128) + (toKingSquare * 2);
        var rookFromIndex = (rookPiece * 128) + (fromRookSquare * 2);
        var rookToIndex = (rookPiece * 128) + (toRookSquare * 2);

        // Precompute bucket offsets
        var wBucketOffset = accumulatorState.WhiteInputBucket * InputBucketWeightCount;
        var bBucketOffset = accumulatorState.BlackInputBucket * InputBucketWeightCount;

        // Directly calculate feature updates using precomputed indices
        var whiteKingFromFeature = WhiteFeatureIndexes[kingFromIndex + whiteMirrored];
        var blackKingFromFeature = BlackFeatureIndexes[kingFromIndex + blackMirrored];
        var whiteKingToFeature = WhiteFeatureIndexes[kingToIndex + whiteMirrored];
        var blackKingToFeature = BlackFeatureIndexes[kingToIndex + blackMirrored];
        var whiteRookFromFeature = WhiteFeatureIndexes[rookFromIndex + whiteMirrored];
        var blackRookFromFeature = BlackFeatureIndexes[rookFromIndex + blackMirrored];
        var whiteRookToFeature = WhiteFeatureIndexes[rookToIndex + whiteMirrored];
        var blackRookToFeature = BlackFeatureIndexes[rookToIndex + blackMirrored];

        // Update the accumulator state with calculated feature updates
        accumulatorState.WhiteSubFeatureUpdatesA = wBucketOffset + whiteKingFromFeature;
        accumulatorState.BlackSubFeatureUpdatesA = bBucketOffset + blackKingFromFeature;
        accumulatorState.WhiteSubFeatureUpdatesB = wBucketOffset + whiteRookFromFeature;
        accumulatorState.BlackSubFeatureUpdatesB = bBucketOffset + blackRookFromFeature;

        accumulatorState.WhiteAddFeatureUpdatesA = wBucketOffset + whiteKingToFeature;
        accumulatorState.BlackAddFeatureUpdatesA = bBucketOffset + blackKingToFeature;
        accumulatorState.WhiteAddFeatureUpdatesB = wBucketOffset + whiteRookToFeature;
        accumulatorState.BlackAddFeatureUpdatesB = bBucketOffset + blackRookToFeature;

        // Set the change type
        accumulatorState.ChangeType = AccumulatorChangeType.SubSubAddAdd;
    }


    public static readonly int* WhiteFeatureIndexes;
    public static readonly int* BlackFeatureIndexes;
    static NnueExtensions()
    {
        WhiteFeatureIndexes = AllocateInt(13 * 64 * 2);
        BlackFeatureIndexes = AllocateInt(13 * 64 * 2);
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

    public static int* AllocateInt(nuint count)
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)sizeof(int) * count, alignment);
        NativeMemory.Clear(block, (nuint)sizeof(int) * count);

        return (int*)block;
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