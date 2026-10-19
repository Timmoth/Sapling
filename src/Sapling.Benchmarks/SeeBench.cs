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
    private List<uint> _preparedCaptures = null!;
    private readonly ulong[] _preparedOccupancy = new ulong[8];

    [GlobalSetup]
    public void Setup()
    {
        _gs = GameState.InitialState();
        _captures = new List<uint>(64);
        _gs.ResetToFen(Positions.Tactical[0]);
        _preparedCaptures = new List<uint>(64);
        MoveGenerator.GenerateLegalMoves(ref _gs.Board, _preparedCaptures, captureOnly: true);
        FillOccupancy(ref _gs.Board, _preparedOccupancy);
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

    // Isolates repeated SEE calls over one pre-parsed board and move list. Setup, legal
    // move generation, and FEN parsing are outside the measured method.
    [Benchmark]
    public int SeePreparedCaptures()
    {
        var sum = 0;
        var scratch = stackalloc short[64];
        fixed (ulong* occupancy = _preparedOccupancy)
        fixed (BoardStateData* board = &_gs.Board)
        {
            foreach (var move in _preparedCaptures)
                sum += StaticExchangeEvaluator.StaticExchangeEvaluation(ref *board, occupancy, scratch, move);
        }
        return sum;
    }

    private static void FillOccupancy(ref BoardStateData board, ulong[] occupancy)
    {
        occupancy[0] = board.Occupancy[Constants.WhitePieces];
        occupancy[1] = board.Occupancy[Constants.BlackPieces];
        occupancy[2] = board.Occupancy[Constants.BlackPawn] | board.Occupancy[Constants.WhitePawn];
        occupancy[3] = board.Occupancy[Constants.BlackKnight] | board.Occupancy[Constants.WhiteKnight];
        occupancy[4] = board.Occupancy[Constants.BlackBishop] | board.Occupancy[Constants.WhiteBishop];
        occupancy[5] = board.Occupancy[Constants.BlackRook] | board.Occupancy[Constants.WhiteRook];
        occupancy[6] = board.Occupancy[Constants.BlackQueen] | board.Occupancy[Constants.WhiteQueen];
        occupancy[7] = board.Occupancy[Constants.BlackKing] | board.Occupancy[Constants.WhiteKing];
    }
}
