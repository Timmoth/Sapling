using System.Runtime.InteropServices;

namespace Sapling.Engine.Evaluation;

public unsafe struct BucketCache
{
    public BoardStateData WhiteBoard = default;
    public readonly VectorShort* WhiteAccumulator;

    public BoardStateData BlackBoard = default;
    public readonly VectorShort* BlackAccumulator;

    public BucketCache()
    {
        WhiteAccumulator = AllocateAccumulator();
        BlackAccumulator = AllocateAccumulator();
    }
    public static VectorShort* AllocateAccumulator()
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)NnueEvaluator.L1ByteSize, alignment);
        NativeMemory.Clear(block, (nuint)NnueEvaluator.L1ByteSize);

        return (VectorShort*)block;
    }
    public void Dispose()
    {
        NativeMemory.AlignedFree(WhiteAccumulator);
        NativeMemory.AlignedFree(BlackAccumulator);
    }
}