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
        var occupancy = stackalloc ulong[8];
        var scratch = stackalloc short[64];
        foreach (var fen in Positions.Tactical)
        {
            _gs.ResetToFen(fen);
            _captures.Clear();
            MoveGenerator.GenerateLegalMoves(ref _gs.Board, _captures, captureOnly: true);

            occupancy[0] = _gs.Board.Occupancy[Constants.WhitePieces];
            occupancy[1] = _gs.Board.Occupancy[Constants.BlackPieces];
            occupancy[2] = _gs.Board.Occupancy[Constants.BlackPawn] | _gs.Board.Occupancy[Constants.WhitePawn];
            occupancy[3] = _gs.Board.Occupancy[Constants.BlackKnight] | _gs.Board.Occupancy[Constants.WhiteKnight];
            occupancy[4] = _gs.Board.Occupancy[Constants.BlackBishop] | _gs.Board.Occupancy[Constants.WhiteBishop];
            occupancy[5] = _gs.Board.Occupancy[Constants.BlackRook] | _gs.Board.Occupancy[Constants.WhiteRook];
            occupancy[6] = _gs.Board.Occupancy[Constants.BlackQueen] | _gs.Board.Occupancy[Constants.WhiteQueen];
            occupancy[7] = _gs.Board.Occupancy[Constants.BlackKing] | _gs.Board.Occupancy[Constants.WhiteKing];
            foreach (var move in _captures)
            {
                sum += StaticExchangeEvaluator.StaticExchangeEvaluation(ref _gs.Board, occupancy, scratch, move);
            }
        }
        return sum;
    }
}
