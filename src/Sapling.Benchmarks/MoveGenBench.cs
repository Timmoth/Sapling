using BenchmarkDotNet.Attributes;
using Sapling.Engine;
using Sapling.Engine.MoveGen;

namespace Sapling.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 8)]
public class MoveGenBench
{
    private GameState _gs = null!;
    private List<uint> _moves = null!;

    [GlobalSetup]
    public void Setup()
    {
        _gs = GameState.InitialState();
        _moves = new List<uint>(256);
    }

    [GlobalCleanup]
    public void Cleanup() => _gs.Dispose();

    [Benchmark(Baseline = true)]
    public int LegalAll()
    {
        var total = 0;
        foreach (var fen in Positions.Middlegame)
        {
            _gs.ResetToFen(fen);
            _moves.Clear();
            MoveGenerator.GenerateLegalMoves(ref _gs.Board, _moves, captureOnly: false);
            total += _moves.Count;
        }
        return total;
    }

    [Benchmark]
    public int LegalCaptures()
    {
        var total = 0;
        foreach (var fen in Positions.Tactical)
        {
            _gs.ResetToFen(fen);
            _moves.Clear();
            MoveGenerator.GenerateLegalMoves(ref _gs.Board, _moves, captureOnly: true);
            total += _moves.Count;
        }
        return total;
    }
}
