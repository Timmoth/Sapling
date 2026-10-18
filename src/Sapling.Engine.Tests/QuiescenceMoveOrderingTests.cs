using Sapling.Engine.DataGen;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Tuning;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Tests;

public unsafe class QuiescenceMoveOrderingTests
{
    private const int TableSize = 1024;

    [Fact]
    public void TtMoveScore_IsNotOverwrittenByCaptureScoring()
    {
        var board = BoardStateExtensions.CreateBoardFromFen("r6k/8/8/8/1p6/8/8/QR5K w - - 0 1");
        var moves = new List<uint>();
        board.GenerateLegalMoves(moves, captureOnly: true);
        var ttMove = Assert.Single(moves, move => move.ToUciMoveName() == "b1b4");
        var higherValueCapture = Assert.Single(moves, move => move.ToUciMoveName() == "a1a8");

        var occupancy = stackalloc ulong[8]
        {
            board.Occupancy[Constants.WhitePieces],
            board.Occupancy[Constants.BlackPieces],
            board.Occupancy[Constants.BlackPawn] | board.Occupancy[Constants.WhitePawn],
            board.Occupancy[Constants.BlackKnight] | board.Occupancy[Constants.WhiteKnight],
            board.Occupancy[Constants.BlackBishop] | board.Occupancy[Constants.WhiteBishop],
            board.Occupancy[Constants.BlackRook] | board.Occupancy[Constants.WhiteRook],
            board.Occupancy[Constants.BlackQueen] | board.Occupancy[Constants.WhiteQueen],
            board.Occupancy[Constants.BlackKing] | board.Occupancy[Constants.WhiteKing]
        };
        var captures = stackalloc short[board.PieceCount];
        var history = stackalloc int[13 * 64];

        var ttScore = Searcher.ScoreQuiescenceMove(ref board, ttMove, ttMove, occupancy, captures, history);
        var otherScore = Searcher.ScoreQuiescenceMove(ref board, higherValueCapture, ttMove,
            occupancy, captures, history);

        Assert.Equal(SpsaOptions.MoveOrderingBestMoveBias, ttScore);
        Assert.True(ttScore > otherScore, "TT priority must survive the capture/SEE scoring branches");
    }

    [Fact]
    public void IllegalTtCapture_IsStillRejectedByLegalityValidation()
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(
            "8/qb6/B7/K1k5/8/1qn5/8/8 w - - 0 1"));
        var pseudoMoves = stackalloc uint[218];
        var count = state.Board.GeneratePseudoLegalMoves(pseudoMoves, captureOnly: true);
        var illegalTtMove = 0u;
        for (var i = 0; i < count; i++)
        {
            if (pseudoMoves[i].ToUciMoveName() == "a6b7")
                illegalTtMove = pseudoMoves[i];
        }
        Assert.NotEqual(0u, illegalTtMove);
        Assert.DoesNotContain(illegalTtMove, state.LegalMoves);

        var table = Bench.AllocateTranspositions(TableSize);
        try
        {
            using var searcher = new Searcher(table, TableSize);
            searcher.Reset(state);
            state.Board.CloneTo(ref searcher.Boards[0]);
            searcher.FillInitialAccumulators(searcher.Boards, searcher.Accumulators);

            ref var entry = ref searcher.Transpositions[state.Board.Hash & searcher.TtMask];
            entry.Set(state.Board.Hash, 0, 0, Constants.MinScore + 1,
                TranspositionTableFlag.Alpha, illegalTtMove, shared: false);
            var score = searcher.QuiescenceSearch(searcher.Boards, searcher.Accumulators, 0,
                Constants.MinScore, Constants.MaxScore);

            Assert.True(score == 0, "the TT move is pseudo-legal but exposes the king, and the position is stalemate");
        }
        finally
        {
            MemoryHelpers.FreeAligned(table);
        }
    }
}
