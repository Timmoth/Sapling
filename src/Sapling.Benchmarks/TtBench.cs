using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Sapling.Engine;
using Sapling.Engine.Transpositions;

namespace Sapling.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 8)]
public unsafe class TtBench
{
    private const int Ops = 4096;
    private Transposition* _tt;
    private ulong _mask;
    private ulong[] _hashes = null!;

    [GlobalSetup]
    public void Setup()
    {
        var ttCount = TranspositionTableExtensions.CalculateTranspositionTableSize(64);
        _tt = MemoryHelpers.Allocate<Transposition>(ttCount);
        NativeMemory.Clear(_tt, (nuint)sizeof(Transposition) * (nuint)ttCount);
        _mask = (ulong)ttCount - 1;
        var rng = new Random(12345);
        _hashes = new ulong[Ops];
        for (var i = 0; i < Ops; i++)
            _hashes[i] = (unchecked((ulong)rng.NextInt64()) << 1) | (uint)rng.Next(2);
    }

    [GlobalCleanup]
    public void Cleanup() => MemoryHelpers.FreeAligned(_tt);

    [Benchmark]
    public int StoreAndProbe()
    {
        var hits = 0;
        for (var i = 0; i < Ops; i++)
        {
            var hash = _hashes[i];
            ref var entry = ref _tt[hash & _mask];
            entry.Set(hash, depth: 12, ply: 4, eval: 37, TranspositionTableFlag.Exact, move: 0x1234, shared: true);
            var snapshot = TranspositionTableExtensions.Probe(ref entry);
            if (snapshot.Valid && snapshot.FullHash == hash && snapshot.Evaluation != TranspositionTableExtensions.NoHashEntry)
                hits++;
        }
        return hits;
    }
}
