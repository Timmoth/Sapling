using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Sapling.Engine;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 8)]
public unsafe class SearchBench
{
    private const int Depth = 10;
    private const int HashMb = 64;
    private static readonly string Position =
        "1r2r2k/1b4q1/pp5p/2pPp1p1/P3Pn2/1P1B1Q1P/2R3P1/4BR1K b - - 1 37";

    private GameState _gameState = null!;
    private Searcher _searcher = null!;
    private Transposition* _transpositions;
    private int _ttCount;
    private nuint _ttBytes;

    [GlobalSetup]
    public void Setup()
    {
        _gameState = GameState.InitialState();
        _gameState.ResetToFen(Position);
        _ttCount = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(HashMb);
        _ttBytes = (nuint)sizeof(Transposition) * (nuint)_ttCount;
        _transpositions = MemoryHelpers.Allocate<Transposition>(_ttCount);
        _searcher = new Searcher(_transpositions, _ttCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher.Dispose();
        MemoryHelpers.FreeAligned(_transpositions);
        _gameState.Dispose();
    }

    // Measures the production Searcher API path for a fixed hard corpus position.
    // Table/history reset is included so each invocation starts from a comparable state.
    [Benchmark]
    public long SearchFixedPosition()
    {
        NativeMemory.Clear(_transpositions, _ttBytes);
        _searcher.Reset(_gameState);
        NativeMemory.Clear(_searcher.PawnCorrHist, (nuint)Searcher.TableElementsSize * sizeof(int));
        NativeMemory.Clear(_searcher.WhiteMaterialCorrHist, (nuint)Searcher.TableElementsSize * sizeof(int));
        NativeMemory.Clear(_searcher.BlackMaterialCorrHist, (nuint)Searcher.TableElementsSize * sizeof(int));
        var (_, _, _, nodes) = _searcher.Search(_gameState, _searcher.Stop,
            depthLimit: Depth, threadId: -1);
        return nodes;
    }
}
