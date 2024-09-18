using System.Runtime.InteropServices;
using Sapling.Engine;

namespace Sapling;

public static unsafe class PVTable
{
    public static readonly int* Indexes;
    public const int IndexCount = Constants.MaxSearchDepth + 16;
    static PVTable()
    {
        Indexes = AllocateULong(IndexCount);
        var previousPvIndex = 0;
        Indexes[0] = previousPvIndex;

        for (var depth = 0; depth < IndexCount - 1; ++depth)
        {
            Indexes[depth + 1] = previousPvIndex + Constants.MaxSearchDepth - depth;
            previousPvIndex = Indexes[depth + 1];
        }
    }

    public static int* AllocateULong(int count)
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc(sizeof(int) * (nuint)count, alignment);
        NativeMemory.Clear(block, sizeof(int) * (nuint)count);

        return (int*)block;
    }
}