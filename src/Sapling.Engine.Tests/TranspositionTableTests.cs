using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Sapling.Engine.Transpositions;
using Xunit;

namespace Sapling.Engine.Tests;

// The stress test uses real threads and Task.WaitAll, which are unavailable on browser-wasm.
// It runs on the native x64 and ARM64 lanes; the skip is reported, not hidden.
public class NativeThreadsFactAttribute : FactAttribute
{
    public NativeThreadsFactAttribute()
    {
        if (OperatingSystem.IsBrowser())
        {
            Skip = "requires native threads / Task.WaitAll; native lanes only";
        }
    }
}

// Finalizer-driven buffer reclamation is a native-lane guarantee; browser-wasm's
// cooperative runtime does not reliably drain the finalizer thread within GC waits.
// The wasm lifecycle contract is explicit Dispose (qualified by the disposal tests,
// which run on all lanes). The skip is reported, not hidden.
public class NativeFinalizerFactAttribute : FactAttribute
{
    public NativeFinalizerFactAttribute()
    {
        if (OperatingSystem.IsBrowser())
        {
            Skip = "requires native finalizer-thread semantics; native lanes only";
        }
    }
}

// A trivially-coherent reference: every slot mutation and read happens under a lock.
// Used to cross-check the lock-free production table's single-threaded behaviour.
public sealed unsafe class LockedTranspositionTable
{
    private readonly Transposition* _tt;
    private readonly ulong _mask;
    private readonly object _gate = new();

    public unsafe LockedTranspositionTable(int entries)
    {
        _tt = MemoryHelpers.Allocate<Transposition>(entries);
        _mask = (ulong)(entries - 1);
    }

    public unsafe void Set(ulong hash, byte depth, int ply, int eval, TranspositionTableFlag flag, uint move)
    {
        lock (_gate)
        {
            ref var entry = ref _tt[hash & _mask];
            if (entry.FullHash == 0 || entry.FullHash != hash || flag == TranspositionTableFlag.Exact || depth >= entry.Depth)
            {
                entry.FullHash = hash;
                entry.Depth = depth;
                entry.Evaluation = eval > 27000 ? eval + ply : eval < -27000 ? eval - ply : eval;
                entry.Flag = flag;
                if (move != 0) entry.Move = move;
            }
        }
    }

    public unsafe TranspositionTableExtensions.TtSnapshot Probe(ulong hash)
    {
        lock (_gate)
        {
            ref var entry = ref _tt[hash & _mask];
            return new TranspositionTableExtensions.TtSnapshot {
                Valid = true, FullHash = entry.FullHash, Evaluation = entry.Evaluation,
                Depth = entry.Depth, Flag = entry.Flag, Move = entry.Move
            };
        }
    }

    public unsafe void Dispose() => NativeMemory.AlignedFree(_tt);
}

public unsafe class TranspositionTableTests
{
    // Encode a fully self-consistent entry from one seed so any torn read is detectable:
    // every field is a pure function of the seed, so a mixed snapshot breaks the relations.
    private const ulong SlotStride = 1024; // keeps all seeds colliding on the same slot (mask 1023)

    private static void WriteSeed(ref Transposition entry, int seed)
    {
        TranspositionTableExtensions.Set(ref entry, (ulong)seed * SlotStride, (byte)seed, 0, seed,
            (TranspositionTableFlag)(seed & 3), (uint)seed);
    }

    private static bool Consistent(TranspositionTableExtensions.TtSnapshot s)
    {
        var seed = (int)s.Move;
        return s.Evaluation == seed
               && s.Depth == (byte)seed
               && s.Flag == (TranspositionTableFlag)(seed & 3)
               && s.FullHash == (ulong)seed * SlotStride;
    }

    [Fact]
    public unsafe void SetThenProbe_ReturnsExactSnapshot()
    {
        var tt = MemoryHelpers.Allocate<Transposition>(1024);
        try
        {
            ref var entry = ref tt[7];
            WriteSeed(ref entry, 1234);
            var s = TranspositionTableExtensions.Probe(ref entry);
            Assert.True(s.Valid);
            Assert.Equal(1234u, s.Move);
            Assert.Equal(1234, s.Evaluation);
            Assert.Equal(1234 & 0xFF, s.Depth);
            Assert.Equal((TranspositionTableFlag)(1234 & 3), s.Flag);
            Assert.Equal(1234UL * SlotStride, s.FullHash);
        }
        finally
        {
            NativeMemory.AlignedFree(tt);
        }
    }

    [Fact]
    public void ProductionTable_MatchesLockedReference_SingleThreaded()
    {
        var raw = MemoryHelpers.Allocate<Transposition>(1024);
        var reference = new LockedTranspositionTable(1024);
        try
        {
            var rng = new Random(20261011);
            for (var i = 0; i < 5000; i++)
            {
                // Stored evaluation is packed as signed i16 (production mate scores
                // stay within +/-29128), so seeds must stay in range.
                var seed = 1 + rng.Next(1, 29000);
                var hash = (ulong)seed * SlotStride;
                TranspositionTableExtensions.Set(ref raw[hash & 1023], hash, (byte)seed, 0, seed,
                    (TranspositionTableFlag)(seed & 3), (uint)seed);
                reference.Set(hash, (byte)seed, 0, seed, (TranspositionTableFlag)(seed & 3), (uint)seed);

                var prod = TranspositionTableExtensions.Probe(ref raw[hash & 1023]);
                var expected = reference.Probe(hash);
                Assert.Equal(expected.Valid, prod.Valid);
                if (prod.Valid)
                {
                    Assert.Equal(expected.FullHash, prod.FullHash);
                    Assert.Equal(expected.Evaluation, prod.Evaluation);
                    Assert.Equal(expected.Depth, prod.Depth);
                    Assert.Equal(expected.Flag, prod.Flag);
                    Assert.Equal(expected.Move, prod.Move);
                }
            }
        }
        finally
        {
            NativeMemory.AlignedFree(raw);
            reference.Dispose();
        }
    }

    [NativeThreadsFact]
    public void ConcurrentReaders_CanProbeStableEntryWithoutClaimingExclusiveAccess()
    {
        var entry = new Transposition[1];
        TranspositionTableExtensions.Set(ref entry[0], 0x1234UL, 12, 0, 42,
            TranspositionTableFlag.Exact, 5678, shared: true);
        var misses = 0;

        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                var snapshot = TranspositionTableExtensions.Probe(ref entry[0], shared: true);
                if (!snapshot.Valid || snapshot.FullHash != 0x1234UL || snapshot.Move != 5678)
                    Interlocked.Increment(ref misses);
            }
        });

        Assert.True(misses == 0, "stable reads should not contend with one another or mutate the entry sequence");
        Assert.Equal(0, (entry[0].Version & 1));
    }

    [NativeThreadsFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("browser")]
    public void ConcurrentProbe_NeverReturnsTornSnapshot()
    {
        // All threads contend on one slot (hash & 1023 == 0). Writers publish self-consistent
        // tuples; readers must never observe a snapshot mixing fields from different writers.
        const int writers = 4;
        const int readers = 4;
        const int iterations = 200_000;
        const int range = 6000; // writer sub-range; writers*range stays under the 27000 mate limit
        var tt = MemoryHelpers.Allocate<Transposition>(1024);
        var stop = false;
        var torn = 0;
        var hits = 0;
        try
        {
            var tasks = new List<Task>();
            for (var w = 0; w < writers; w++)
            {
                var offset = w * range;
                tasks.Add(Task.Run(() =>
                {
                    for (var i = 1; i <= iterations && !Volatile.Read(ref stop); i++)
                    {
                        // Cycle within a disjoint, non-mate seed range so Evaluation is stored verbatim.
                        WriteSeed(ref tt[0], offset + (i % range) + 1);
                    }
                }));
            }

            for (var r = 0; r < readers; r++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var localHits = 0;
                    var localTorn = 0;
                    for (var i = 0; i < iterations * 2; i++)
                    {
                        var s = TranspositionTableExtensions.Probe(ref tt[0]);
                        if (!s.Valid)
                        {
                            continue; // torn/in-progress: correctly treated as a miss
                        }

                        localHits++;
                        if (!Consistent(s))
                        {
                            localTorn++;
                        }
                    }

                    Interlocked.Add(ref hits, localHits);
                    Interlocked.Add(ref torn, localTorn);
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Volatile.Write(ref stop, true);

            Assert.True(torn == 0, "the access guard must never expose a mixed (torn) snapshot");
            Assert.True(hits > 0);
        }
        finally
        {
            Volatile.Write(ref stop, true);
            NativeMemory.AlignedFree(tt);
        }
    }
}
