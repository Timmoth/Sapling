using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Sapling.Engine;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 8)]
public unsafe class NnueEvalBench
{
    private GameState _gs = null!;
    private Searcher _searcher = null!;
    private Transposition* _tt;
    private static readonly nuint AccumSize = (nuint)sizeof(AccumulatorState);

    [GlobalSetup]
    public void Setup()
    {
        _gs = GameState.InitialState();
        var ttCount = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(64);
        _tt = MemoryHelpers.Allocate<Transposition>(ttCount);
        _searcher = new Searcher(_tt, ttCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher.Dispose();
        MemoryHelpers.FreeAligned(_tt);
        _gs.Dispose();
    }

    // Full accumulator refresh + forward pass, i.e. a cold-node eval. The
    // accumulator is cleared each call (the same init path Searcher uses per
    // search) so the cached-eval fast path never short-circuits the kernel.
    [Benchmark]
    public int FullEval()
    {
        var sum = 0;
        foreach (var fen in Positions.Middlegame)
        {
            _gs.ResetToFen(fen);
            NativeMemory.Clear(_searcher.Accumulators, AccumSize);
            fixed (BoardStateData* board = &_gs.Board)
            {
                sum += _searcher.Evaluate(board, _searcher.Accumulators, depthFromRoot: 0);
            }
        }
        return sum;
    }
}
