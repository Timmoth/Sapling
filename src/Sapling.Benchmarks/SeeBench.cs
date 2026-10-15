using BenchmarkDotNet.Attributes;
using Sapling.Engine;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;

namespace Sapling.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 8)]
public unsafe class SeeBench
{
    private GameState _gs = null!;
    private List<uint> _captures = null!;

    [GlobalSetup]
    public void Setup()
    {
        _gs = GameState.InitialState();
        _captures = new List<uint>(64);
    }

    [GlobalCleanup]
    public void Cleanup() => _gs.Dispose();

    [Benchmark]
    public int SeeAllCaptures()
    {
        var sum = 0;
        foreach (var fen in Positions.Tactical)
        {
            _gs.ResetToFen(fen);
            _captures.Clear();
            MoveGenerator.GenerateLegalMoves(ref _gs.Board, _captures, captureOnly: true);

            var occupancy = stackalloc ulong[8]
            {
                _gs.Board.Occupancy[Constants.WhitePieces],
                _gs.Board.Occupancy[Constants.BlackPieces],
                _gs.Board.Occupancy[Constants.BlackPawn] | _gs.Board.Occupancy[Constants.WhitePawn],
                _gs.Board.Occupancy[Constants.BlackKnight] | _gs.Board.Occupancy[Constants.WhiteKnight],
                _gs.Board.Occupancy[Constants.BlackBishop] | _gs.Board.Occupancy[Constants.WhiteBishop],
                _gs.Board.Occupancy[Constants.BlackRook] | _gs.Board.Occupancy[Constants.WhiteRook],
                _gs.Board.Occupancy[Constants.BlackQueen] | _gs.Board.Occupancy[Constants.WhiteQueen],
                _gs.Board.Occupancy[Constants.BlackKing] | _gs.Board.Occupancy[Constants.WhiteKing],
            };

            var scratch = stackalloc short[64];
            foreach (var move in _captures)
            {
                sum += StaticExchangeEvaluator.StaticExchangeEvaluation(ref _gs.Board, occupancy, scratch, move);
            }
        }
        return sum;
    }
}
