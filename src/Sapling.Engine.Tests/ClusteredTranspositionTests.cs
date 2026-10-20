using System.Runtime.InteropServices;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Tests;

public unsafe class ClusteredTranspositionTests
{
    [Fact]
    public void FourCollidingPositionsSurviveAndFifthEvictsShallowest()
    {
        var table = MemoryHelpers.Allocate<Transposition>(16);
        try
        {
            for (byte i = 0; i < 4; i++)
                TranspositionTableExtensions.SetCluster(table, 15, (ulong)(1 + 16 * i),
                    (byte)(i + 1), 0, i, TranspositionTableFlag.Exact, (uint)(i + 1), 1);
            for (var i = 0; i < 4; i++)
                Assert.True(TranspositionTableExtensions.ProbeCluster(table,
                    (ulong)(1 + 16 * i), 15, 1, out _).Valid);
            TranspositionTableExtensions.SetCluster(table, 15, 65, 8, 0, 42,
                TranspositionTableFlag.Exact, 42, 1);
            Assert.False(TranspositionTableExtensions.ProbeCluster(table, 1, 15, 1, out _).Valid);
            Assert.True(TranspositionTableExtensions.ProbeCluster(table, 65, 15, 1, out _).Valid);
        }
        finally { MemoryHelpers.FreeAligned(table); }
    }

    [Fact]
    public void StaleDeepEntryLosesToFreshShallowEntriesAcrossGenerationWrap()
    {
        var table = MemoryHelpers.Allocate<Transposition>(16);
        try
        {
            TranspositionTableExtensions.SetCluster(table, 15, 1, 32, 0, 1,
                TranspositionTableFlag.Exact, 1, 250);
            for (var i = 1; i < 4; i++)
                TranspositionTableExtensions.SetCluster(table, 15, (ulong)(1 + 16 * i),
                    2, 0, i, TranspositionTableFlag.Exact, (uint)i, 2);
            TranspositionTableExtensions.SetCluster(table, 15, 65, 1, 0, 65,
                TranspositionTableFlag.Exact, 65, 2);
            Assert.False(TranspositionTableExtensions.ProbeCluster(table, 1, 15, 2, out _).Valid);
            Assert.True(TranspositionTableExtensions.ProbeCluster(table, 17, 15, 2, out _).Valid);
        }
        finally { MemoryHelpers.FreeAligned(table); }
    }

    [Fact]
    public void PackedMoveAndNegativeScoreDoNotOverlapGenerationOrSequence()
    {
        Assert.Equal(16, Marshal.SizeOf<Transposition>());
        var entry = new Transposition { FullHash = 0xDEADBEEF,
            Stamp = Transposition.PackStamp(0xFFFFFF, -12345, 120,
                TranspositionTableFlag.Beta, 62, 255) };
        var snapshot = TranspositionTableExtensions.Probe(ref entry);
        Assert.True(snapshot.Valid);
        Assert.Equal(0xFFFFFFu, snapshot.Move);
        Assert.Equal(-12345, snapshot.Evaluation);
        Assert.Equal(120, snapshot.Depth);
        Assert.Equal(255, snapshot.Generation);
        Assert.Equal(62, entry.Version);
    }
}
