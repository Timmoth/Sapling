using System.Collections.Immutable;
using Sapling.Engine;

namespace Sapling;

public static class PVTable
{
    public static readonly ImmutableArray<int> Indexes = Initialize();

    private static ImmutableArray<int> Initialize()
    {
        var indexes = new int[Constants.MaxSearchDepth + 16];
        var previousPvIndex = 0;
        indexes[0] = previousPvIndex;

        for (var depth = 0; depth < indexes.Length - 1; ++depth)
        {
            indexes[depth + 1] = previousPvIndex + Constants.MaxSearchDepth - depth;
            previousPvIndex = indexes[depth + 1];
        }

        return ImmutableArray.Create(indexes);
    }
}
