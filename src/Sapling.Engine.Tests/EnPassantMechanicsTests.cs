using Sapling.Engine;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

/// <summary>
/// Regression cases for en-passant handling in SEE and position hashing.
/// </summary>
public unsafe class EnPassantMechanicsTests
{
    [Fact]
    public void See_EnPassant_RemovesCapturedPawnFromExchangeOccupancy()
    {
        // White Ke1, pawn e5, rook d4; Black Kd8, pawn d5 (double-pushed; ep target d6),
        // pawn c7. exd6 (e.p.) captures the d5 pawn and lands on d6. After the capture
        // the d-file is open so the d4 rook defends d6:
        //   pawn takes pawn (100), c7 pawn takes (0), rook takes (650) -> fold = +100.
        // The captured pawn on d5 must be removed so the rook's defense is visible.
        var board = BoardStateExtensions.CreateBoardFromFen("3k4/2p5/8/3pP3/3R4/8/8/3K4 w - d6 0 1");

        var moves = stackalloc uint[218];
        var count = board.GeneratePseudoLegalMoves(moves, false);
        var move = 0u;
        for (var i = 0; i < count; i++)
        {
            if (moves[i].ToUciMoveName() == "e5d6")
            {
                move = moves[i];
                break;
            }
        }

        Assert.True(move != 0, "e5d6 en passant must be generated");
        Assert.True(move.IsEnPassant());

        var occ = stackalloc ulong[8]
        {
            board.Occupancy[Constants.WhitePieces],
            board.Occupancy[Constants.BlackPieces],
            board.Occupancy[Constants.BlackPawn] | board.Occupancy[Constants.WhitePawn],
            board.Occupancy[Constants.BlackKnight] | board.Occupancy[Constants.WhiteKnight],
            board.Occupancy[Constants.BlackBishop] | board.Occupancy[Constants.WhiteBishop],
            board.Occupancy[Constants.BlackRook] | board.Occupancy[Constants.WhiteRook],
            board.Occupancy[Constants.BlackQueen] | board.Occupancy[Constants.WhiteQueen],
            board.Occupancy[Constants.BlackKing] | board.Occupancy[Constants.WhiteKing],
        };
        var scratch = stackalloc short[board.PieceCount];

        var see = board.StaticExchangeEvaluation(occ, scratch, move);
        Assert.True(see == 100, "the captured d5 pawn must be removed from the exchange occupancy so Rd4 defends d6");
    }

    [Fact]
    public void Hash_OmitsEnPassantFile_WhenNoCapturePossible()
    {
        // The white pawn on e4 records a plausible e2-e4 double push. Black has no
        // adjacent pawn, so the FEN ep target is not part of position identity.
        var withEp = BoardStateExtensions.CreateBoardFromFen("r6k/8/8/8/4P3/8/8/R1K5 b - e3 0 1");
        var noEp = BoardStateExtensions.CreateBoardFromFen("r6k/8/8/8/4P3/8/8/R1K5 b - - 0 1");

        Assert.True(withEp.EnPassantFile.CanEnPassant());

        var epCapturePossible = false;
        var moves = new List<uint>();
        withEp.GenerateLegalMoves(moves, false);
        foreach (var m in moves)
        {
            if (m.IsEnPassant())
            {
                epCapturePossible = true;
            }
        }

        Assert.False(epCapturePossible, "black has no pawn able to capture en passant");
        Assert.True(withEp.Hash == noEp.Hash, "positions differing only in an unusable ep file must hash equal (repetition/TT correctness)");
    }

    [Fact]
    public void Hash_IncludesEnPassantFile_WhenCaptureIsPossible()
    {
        var withEp = BoardStateExtensions.CreateBoardFromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        var noEp = BoardStateExtensions.CreateBoardFromFen("4k3/8/8/3pP3/8/8/8/4K3 w - - 0 1");

        Assert.Equal(withEp.EnPassantFile, Zobrist.GetEnPassantHashFile(ref withEp));
        Assert.True(withEp.Hash != noEp.Hash, "a legal en-passant capture changes the available moves and therefore position identity");
    }

    [Fact]
    public void Hash_OmitsEnPassantFile_WhenEveryCaptureExposesOwnKing()
    {
        var withEp = BoardStateExtensions.CreateBoardFromFen("k7/8/8/r4pPK/8/8/8/8 w - f6 0 1");
        var noEp = BoardStateExtensions.CreateBoardFromFen("k7/8/8/r4pPK/8/8/8/8 w - - 0 1");
        var legalMoves = new List<uint>();
        withEp.GenerateLegalMoves(legalMoves, false);

        Assert.False(legalMoves.Any(m => m.IsEnPassant()), "g5xf6 would uncover the rook attack on Kh5");
        Assert.Equal(8, Zobrist.GetEnPassantHashFile(ref withEp));
        Assert.True(withEp.Hash == noEp.Hash, "a pinned pawn cannot legally capture en passant, so the positions have the same legal moves");
    }

    [Theory]
    [InlineData("k7/8/8/r4pPK/8/8/8/8 w - f6 0 1")] // pseudo-capture is pinned and illegal
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")] // legal en-passant capture
    public void NullMove_LeavesHashConsistent_WhenClearingEnPassant(string fen)
    {
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        Assert.True(board.Hash == Zobrist.CalculateZobristKey(ref board), "the parsed hash is the reference");

        board.ApplyNullMove();

        Assert.Equal(8, board.EnPassantFile);
        Assert.True(board.Hash == Zobrist.CalculateZobristKey(ref board), "null move must remove the EP hash component only when it was actually present");
    }

    [Fact]
    public void ThreefoldRepetition_IgnoresUnusableEnPassantTarget()
    {
        // The same position (placement, side to move, castling) occurs three times:
        //   after 1. e4   (ep file set, no black pawn can capture: unusable)
        //   after 3. Kc1  (no ep)
        //   after 5. Kc1  (no ep)
        // The black king on h8 makes this a valid position; the initial e2-e4
        // ep target is unusable because black has no adjacent pawn.
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen("r6k/8/8/8/8/8/4P3/R1K5 w - - 0 1"));

        foreach (var uci in new[] { "e2e4", "a8a7", "c1b1", "a7a8", "b1c1", "a8a7", "c1b1", "a7a8", "b1c1" })
        {
            var move = Assert.Single(state.LegalMoves, m => m.ToUciMoveName() == uci);
            state.Apply(move);
        }

        Assert.True(state.HashHistory[1] == state.HashHistory[5], "the first and second matching positions should hash equally");
        Assert.True(state.HashHistory[5] == state.HashHistory[9], "the second and third matching positions should hash equally");
        Assert.True(RepetitionDetector.IsThreefoldRepetition(state.Board.TurnCount, state.Board.HalfMoveClock, state.HashHistory), "the position occurred three times; the first occurrence only differs by an unusable ep file");
    }
}
