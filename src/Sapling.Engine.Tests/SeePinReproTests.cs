using Sapling.Engine;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

// SEE must exclude illegal pinned recaptures while retaining legal exchanges.
public unsafe class SeePinReproTests
{
    private static int See(string fen, string uciMove)
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
        var board = state.Board;
        var moves = stackalloc uint[218];
        var count = board.GeneratePseudoLegalMoves(moves, false);
        var move = 0u;
        for (var i = 0; i < count; i++)
        {
            if (moves[i].ToUciMoveName() == uciMove)
            {
                move = moves[i];
                break;
            }
        }

        Assert.True(move != 0, $"move {uciMove} must be pseudo-legal in {fen}");
        Assert.True(state.LegalMoves.Contains(move), $"SEE inputs must be legal chess moves in {fen}");

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
        return board.StaticExchangeEvaluation(occ, scratch, move);
    }

    // Deliberately separate from SEE's attacker/pin bitboards. This reference
    // enumerates legal captures to the exchange square, applies them to a copied
    // board, and uses minimax with the option to stop exchanging at every ply.
    private static int LegalExchangeOracle(string fen, string uciMove)
    {
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        var legalMoves = new List<uint>();
        board.GenerateLegalMoves(legalMoves, captureOnly: true);
        var root = Assert.Single(legalMoves, move => move.ToUciMoveName() == uciMove);
        var capturedValue = PieceValues.PieceValue[root.GetCapturedPiece()];
        var target = root.GetToSquare();
        var afterRoot = ApplyLegalMove(board, root);
        return capturedValue - BestLegalRecapture(ref afterRoot, target, 0);
    }

    private static int BestLegalRecapture(ref BoardStateData board, int target, int ply)
    {
        if (ply >= board.PieceCount)
            return 0;

        var legalMoves = new List<uint>();
        board.GenerateLegalMoves(legalMoves, captureOnly: true);
        var best = 0; // The side may always decline another exchange.
        foreach (var move in legalMoves)
        {
            if (move.GetToSquare() != target || move.GetCapturedPiece() == Constants.None)
                continue;

            var gain = (int)PieceValues.PieceValue[move.GetCapturedPiece()];
            var afterCapture = ApplyLegalMove(board, move);
            best = Math.Max(best, gain - BestLegalRecapture(ref afterCapture, target, ply + 1));
        }

        return best;
    }

    private static BoardStateData ApplyLegalMove(BoardStateData board, uint move)
    {
        var movingSide = board.WhiteToMove;
        var applied = movingSide ? board.PartialApplyWhite(move) : board.PartialApplyBlack(move);
        Assert.True(applied, "the independent exchange oracle only applies legal moves");
        board.WhiteToMove = !movingSide;
        board.UpdateCheckStatus();
        return board;
    }

    [Theory]
    [InlineData("k2q4/1n6/8/2PR4/8/8/8/3K4 b - - 0 1", "b7c5")]
    [InlineData("k2q4/1n6/8/2PR4/8/8/4K3/8 b - - 0 1", "b7c5")]
    [InlineData("3q3k/8/8/3n4/8/2P5/3P4/3K4 b - - 0 1", "d5c3")]
    [InlineData("8/8/4k3/3p4/3Q4/8/8/K2R4 w - - 0 1", "d4d5")]
    [InlineData("8/8/4k3/3p4/3Q4/8/8/K7 w - - 0 1", "d4d5")]
    [InlineData("k3r3/8/8/3q4/4P3/8/4R3/4K3 b - - 0 1", "d5e4")]
    public void SeeMatchesIndependentLegalExchangeTree(string fen, string uciMove)
    {
        Assert.Equal(LegalExchangeOracle(fen, uciMove), See(fen, uciMove));
    }

    [Fact]
    public void PinnedRookRecapturer_IsExcluded()
    {
        // Black Nxc5 wins a clean pawn: the only white recapturer (Rd5) is pinned
        // by Qd8 against Kd1 and cannot legally recapture.
        var see = See("k2q4/1n6/8/2PR4/8/8/8/3K4 b - - 0 1", "b7c5");
        Assert.True(see == 100, "pinned rook cannot recapture: free pawn");
    }

    [Fact]
    public void UnpinnedRookRecapturer_IsCorrect_Control()
    {
        // Same position with the white king off the d-file: the rook recapture is
        // legal, so the exchange loses the knight for a pawn.
        var see = See("k2q4/1n6/8/2PR4/8/8/4K3/8 b - - 0 1", "b7c5");
        Assert.True(see == -350, "unpinned rook recapture is legal: knight loses to pawn+rook");
    }

    [Fact]
    public void PinnedPawnRecapturer_IsExcluded()
    {
    // Pinned pawn recapturer: the d2 pawn is pinned by Qd8 against Kd1, so
        // black Nxc3 wins a clean pawn.
        var see = See("3q3k/8/8/3n4/8/2P5/3P4/3K4 b - - 0 1", "d5c3");
        Assert.True(see == 100, "pinned pawn cannot recapture: free pawn");
    }

    [Fact]
    public void PinnedRookRecapturer_CanCaptureAlongPinRay()
    {
        // Qxe4 removes the pawn that was shielding the e-file. Re2 is now
        // absolutely pinned to Ke1 by Re8, but Rxe4 is legal because it stays
        // between the king and the pinning rook. Re8 can then recapture.
        const string fen = "k3r3/8/8/3q4/4P3/8/4R3/4K3 b - - 0 1";
        var expected = LegalExchangeOracle(fen, "d5e4");
        Assert.Equal(Constants.PawnValue - Constants.QueenValue + Constants.RookValue, expected);
        Assert.True(See(fen, "d5e4") == expected, "both the legal pin-ray recapture and the later rook recapture must be counted");
    }

    [Fact]
    public void NewlyRevealedPin_IsRecomputedAfterEarlierExchangeCapture()
    {
        // ...cxb3, Bxb3, Rxb3 removes Ba2 from the a-file. Ba4xb3 is pseudo-legal
        // but would expose Ka1 to Ra8, so the newly pinned bishop cannot recapture.
        const string fen = "rr5k/8/8/8/B1p5/1P6/B7/K7 b - - 0 1";
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        foreach (var uci in new[] { "c4b3", "a2b3", "b8b3" })
        {
            var legalMoves = new List<uint>();
            board.GenerateLegalMoves(legalMoves, captureOnly: false);
            var move = Assert.Single(legalMoves, candidate => candidate.ToUciMoveName() == uci);
            board = ApplyLegalMove(board, move);
        }

        var pseudoMoves = stackalloc uint[218];
        var pseudoCount = board.GeneratePseudoLegalMoves(pseudoMoves, captureOnly: true);
        var illegalRecapture = 0u;
        for (var i = 0; i < pseudoCount; i++)
        {
            if (pseudoMoves[i].ToUciMoveName() == "a4b3")
                illegalRecapture = pseudoMoves[i];
        }
        Assert.NotEqual(0u, illegalRecapture);
        var legalAfterSequence = new List<uint>();
        board.GenerateLegalMoves(legalAfterSequence, captureOnly: true);
        Assert.DoesNotContain(illegalRecapture, legalAfterSequence);

        var oracle = LegalExchangeOracle(fen, "c4b3");
        Assert.True(oracle == Constants.PawnValue, "the side can decline its losing first recapture; the later pinned bishop is illegal");
        Assert.Equal(oracle, See(fen, "c4b3"));
    }

    [Fact]
    public void KingRecapture_IsRejectedWhenTargetIsDefended()
    {
        var see = See("8/8/4k3/3p4/3Q4/8/8/K2R4 w - - 0 1", "d4d5");
        Assert.True(see == 100, "the rook protects d5, so Kxd5 is illegal");
    }

    [Fact]
    public void KingRecapture_IsCountedWhenTargetIsSafe()
    {
        var see = See("8/8/4k3/3p4/3Q4/8/8/K7 w - - 0 1", "d4d5");
        Assert.True(see == Constants.PawnValue - Constants.QueenValue, "the king can safely capture the queen on d5");
    }

    [Fact]
    public void BishopOnRookRay_DoesNotPinAnotherBishop()
    {
        // Bg8 lies on a rook ray from Ke8, but it cannot pin Bf8. Bf8xc5 is a
        // legal recapture after B a3xc5, so White loses the bishop for a pawn.
        const string fen = "r2qkbBr/7p/Pn3np1/2p4P/p3p1b1/B2p1NP1/2PQ1P2/3RK1R1 w - - 0 73";
        var expected = LegalExchangeOracle(fen, "a3c5");
        Assert.Equal(Constants.PawnValue - Constants.BishopValue, expected);
        Assert.Equal(expected, See(fen, "a3c5"));
    }

    [Fact]
    public void EnPassantCapture_HasFiniteCapturedPawnValue()
    {
        var see = See("7k/8/8/3pP3/8/8/8/K7 w - d6 0 1", "e5d6");
        Assert.True(see == Constants.PawnValue, "en passant is a pawn capture; search move ordering handles it without SEE");
    }

    [Fact]
    public void PromotionCapture_UsesPawnAsInitialMoverForSee()
    {
        var see = See("k6r/6P1/8/8/8/8/8/K7 w - - 0 1", "g7h8q");
        Assert.True(see == Constants.RookValue, "the captured rook is gained; promotion value is handled separately by move ordering");
    }

    [Fact]
    public void UnrecapturedCapture_ReturnsFullCapturedPieceValue()
    {
        // The capturing knight survives, so its value is not subtracted from the
        // captured queen when no legal recapture exists.
        Assert.Equal(Constants.QueenValue, See("7k/8/8/8/1n6/8/2Q5/K7 b - - 0 1", "b4c2"));
    }

    [Fact]
    public void ExchangeTree_ContinuesAfterRecaptureWhenTheNextCaptureReducesTheLoss()
    {
        // Qxe4, Re2xe4, Re8xe4: Black takes a pawn, loses the queen, then wins
        // the rook. The exact balance is +100 - 1250 + 650 = -500.
        const string fen = "k3r3/8/8/3q4/4P3/8/4R3/4K3 b - - 0 1";
        var expected = LegalExchangeOracle(fen, "d5e4");
        Assert.Equal(Constants.PawnValue - Constants.QueenValue + Constants.RookValue, expected);
        Assert.True(See(fen, "d5e4") == expected, "SEE must continue through the legal second recapture rather than stop early");
    }

}
