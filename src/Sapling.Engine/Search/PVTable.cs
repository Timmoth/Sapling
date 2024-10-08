using System.Runtime.InteropServices;
using Sapling.Engine;
using Sapling.Engine.Transpositions;

namespace Sapling;

public static unsafe class PVTable
{
    public static readonly int* Indexes;
    public const int IndexCount = Constants.MaxSearchDepth + 16;
    static PVTable()
    {
        Indexes = MemoryHelpers.Allocate<int>(IndexCount);
        var previousPvIndex = 0;
        Indexes[0] = previousPvIndex;

        for (var depth = 0; depth < IndexCount - 1; ++depth)
        {
            Indexes[depth + 1] = previousPvIndex + Constants.MaxSearchDepth - depth;
            previousPvIndex = Indexes[depth + 1];
        }
    }
}