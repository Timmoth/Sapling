using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Sapling.Engine.Evaluation;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    private static void RefreshAccumulatorTiled(BoardStateData* board, BoardStateData* cachedBoard,
        short* cache, short* destination, int* featureIndexes, int bucket)
    {
        // At most 64 occupied squares can be added/removed, including both kings.
        var adds = stackalloc short*[64];
        var subs = stackalloc short*[64];
        var addCount = 0;
        var subCount = 0;
        var cold = cachedBoard->PieceCount == 0;
        var weights = NnueWeights.FeatureWeights + bucket * InputBucketWeightCount;
        for (var piece = (int)Constants.BlackPawn; piece <= Constants.WhiteKing; piece++)
        {
            var before = cold ? 0UL : cachedBoard->Occupancy[piece];
            var after = board->Occupancy[piece];
            var added = after & ~before;
            var removed = before & ~after;
            while (added != 0)
                adds[addCount++] = weights + featureIndexes[(piece << 7) + (added.PopLSB() << 1)];
            while (removed != 0)
                subs[subCount++] = weights + featureIndexes[(piece << 7) + (removed.PopLSB() << 1)];
        }
        ApplyRefreshDeltas(cold ? NnueWeights.FeatureBiases : cache, cache, destination,
            adds, addCount, subs, subCount);
        Unsafe.CopyBlock(cachedBoard, board, BoardStateData.BoardStateSize);
    }

    // The low i16 result of each feature addition/subtraction wraps, just as the
    // existing kernels do. Combining deltas changes scheduling, not arithmetic.
    internal static void ApplyRefreshDeltas(short* source, short* cache, short* destination,
        short** adds, int addCount, short** subs, int subCount)
    {
        if (addCount == 0 && subCount == 0)
        {
            Unsafe.CopyBlock(destination, source, L1ByteSize);
            if (source != cache) Unsafe.CopyBlock(cache, source, L1ByteSize);
            return;
        }
        if (EngineCapabilities.UseX86Nnue)
        {
            var lanes = VectorShort.Count;
            for (var i = 0; i < NnueWeights.Layer1Size; i += 4 * lanes)
            {
                var v0 = VectorType.Load(source + i + 0 * lanes);
                var v1 = VectorType.Load(source + i + 1 * lanes);
                var v2 = VectorType.Load(source + i + 2 * lanes);
                var v3 = VectorType.Load(source + i + 3 * lanes);
                for (var feature = 0; feature < addCount; feature++)
                {
                    var weights = adds[feature] + i;
                    v0 += VectorType.Load(weights + 0 * lanes);
                    v1 += VectorType.Load(weights + 1 * lanes);
                    v2 += VectorType.Load(weights + 2 * lanes);
                    v3 += VectorType.Load(weights + 3 * lanes);
                }
                for (var feature = 0; feature < subCount; feature++)
                {
                    var weights = subs[feature] + i;
                    v0 -= VectorType.Load(weights + 0 * lanes);
                    v1 -= VectorType.Load(weights + 1 * lanes);
                    v2 -= VectorType.Load(weights + 2 * lanes);
                    v3 -= VectorType.Load(weights + 3 * lanes);
                }
                v0.Store(cache + i + 0 * lanes);
                v0.Store(destination + i + 0 * lanes);
                v1.Store(cache + i + 1 * lanes);
                v1.Store(destination + i + 1 * lanes);
                v2.Store(cache + i + 2 * lanes);
                v2.Store(destination + i + 2 * lanes);
                v3.Store(cache + i + 3 * lanes);
                v3.Store(destination + i + 3 * lanes);
            }
            return;
        }
        if (EngineCapabilities.UseVector128Nnue)
        {
            var lanes = Vector128<short>.Count;
            for (var i = 0; i < NnueWeights.Layer1Size; i += 4 * lanes)
            {
                var v0 = Vector128.Load(source + i + 0 * lanes);
                var v1 = Vector128.Load(source + i + 1 * lanes);
                var v2 = Vector128.Load(source + i + 2 * lanes);
                var v3 = Vector128.Load(source + i + 3 * lanes);
                for (var feature = 0; feature < addCount; feature++)
                {
                    var weights = adds[feature] + i;
                    v0 += Vector128.Load(weights + 0 * lanes);
                    v1 += Vector128.Load(weights + 1 * lanes);
                    v2 += Vector128.Load(weights + 2 * lanes);
                    v3 += Vector128.Load(weights + 3 * lanes);
                }
                for (var feature = 0; feature < subCount; feature++)
                {
                    var weights = subs[feature] + i;
                    v0 -= Vector128.Load(weights + 0 * lanes);
                    v1 -= Vector128.Load(weights + 1 * lanes);
                    v2 -= Vector128.Load(weights + 2 * lanes);
                    v3 -= Vector128.Load(weights + 3 * lanes);
                }
                v0.Store(cache + i + 0 * lanes);
                v0.Store(destination + i + 0 * lanes);
                v1.Store(cache + i + 1 * lanes);
                v1.Store(destination + i + 1 * lanes);
                v2.Store(cache + i + 2 * lanes);
                v2.Store(destination + i + 2 * lanes);
                v3.Store(cache + i + 3 * lanes);
                v3.Store(destination + i + 3 * lanes);
            }
            return;
        }
        for (var i = 0; i < NnueWeights.Layer1Size; i++)
        {
            var value = source[i];
            for (var feature = 0; feature < addCount; feature++)
                value = unchecked((short)(value + adds[feature][i]));
            for (var feature = 0; feature < subCount; feature++)
                value = unchecked((short)(value - subs[feature][i]));
            cache[i] = destination[i] = value;
        }
    }
}
