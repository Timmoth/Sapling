using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Evaluation;

public static class StaticExchangeEvaluator
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int StaticExchangeEvaluation(this ref BoardStateData board, ulong* occupancyBitBoards,
        short* captures, uint move)
    {
        *captures = *(PieceValues.PieceValue + move.GetCapturedPiece());
        var lastPieceValue = *(PieceValues.PieceValue + move.GetMovedPiece());

        var targetSquare = move.GetToSquare();
        // all pieces except the two involved in the initial move
        var occupancy = board.Occupancy[Constants.Occupancy] & ~((1UL << move.GetFromSquare()) | (1UL << targetSquare));
        if (move.IsEnPassant())
        {
            // The captured pawn is not on the destination square. Remove it before
            // finding x-rays so sliders behind it can defend or recapture on target.
            var capturedSquare = targetSquare + (board.WhiteToMove ? -8 : 8);
            occupancy &= ~(1UL << capturedSquare);
        }

        // Bit boards by piece type
        var pawns = *(occupancyBitBoards + 2);
        var knights = *(occupancyBitBoards + 3);
        var bishops = *(occupancyBitBoards + 4);
        var rooks = *(occupancyBitBoards + 5);
        var queens = *(occupancyBitBoards + 6);
        var kings = *(occupancyBitBoards + 7);

        // Calculate all squares that can attack the target square
        var attackers = ((AttackTables.BishopAttacks(occupancy, targetSquare) & (queens | bishops)) |
                         (AttackTables.RookAttacks(occupancy, targetSquare) & (queens | rooks)) |
                         (*(AttackTables.KnightAttackTable + targetSquare) & knights) |
                         (*(AttackTables.WhitePawnAttackTable + targetSquare) & board.Occupancy[Constants.BlackPawn]) |
                         (*(AttackTables.BlackPawnAttackTable + targetSquare) & board.Occupancy[Constants.WhitePawn]) |
                          (*(AttackTables.KingAttackTable + targetSquare) & kings)) & occupancy;

        // Squares that lie on a rank/file/diagonal ray to the target (occupancy-ignoring).
        // A discovered slider attack on the target can only appear when a piece is removed
        // from one of these squares, so the slider recompute below is skipped otherwise.
        // This keeps the SEE result bit-identical while avoiding most slider lookups.
        var aligned = AttackTables.RookAttackMasksAll[targetSquare] | AttackTables.BishopAttackMasksAll[targetSquare];

        // Pinned pieces may not join the exchange unless the target square lies on their
        // pin line (moving along the line keeps the king shielded).
        var whiteKingSquare = (int)System.Numerics.BitOperations.TrailingZeroCount(board.Occupancy[Constants.WhiteKing]);
        var blackKingSquare = (int)System.Numerics.BitOperations.TrailingZeroCount(board.Occupancy[Constants.BlackKing]);
        var whitePieces = *(occupancyBitBoards + 0) & occupancy;
        var blackPieces = *(occupancyBitBoards + 1) & occupancy;
        var pinLineWhite = LineThrough(whiteKingSquare, targetSquare);
        var pinLineBlack = LineThrough(blackKingSquare, targetSquare);
        var kingRayWhite = AttackTables.RookAttackMasksAll[whiteKingSquare] |
                           AttackTables.BishopAttackMasksAll[whiteKingSquare];
        var kingRayBlack = AttackTables.RookAttackMasksAll[blackKingSquare] |
                           AttackTables.BishopAttackMasksAll[blackKingSquare];
        // No enemy sliders => no pins possible; skip the two slider probes in ComputePinned.
        var whitePinnerRQ = blackPieces & (rooks | queens);
        var whitePinnerBQ = blackPieces & (bishops | queens);
        var blackPinnerRQ = whitePieces & (rooks | queens);
        var blackPinnerBQ = whitePieces & (bishops | queens);
        var pinOccupancy0 = occupancy | (1UL << targetSquare);
        var pinnedWhite = (whitePinnerRQ | whitePinnerBQ) == 0
            ? 0UL
            : ComputePinned(whiteKingSquare, whitePieces, whitePinnerRQ, whitePinnerBQ, pinOccupancy0);
        var pinnedBlack = (blackPinnerRQ | blackPinnerBQ) == 0
            ? 0UL
            : ComputePinned(blackKingSquare, blackPieces, blackPinnerRQ, blackPinnerBQ, pinOccupancy0);

        // Starts off as the opponents turn
        var turn = board.WhiteToMove ? 1 : 0;
        var cIndex = 1;
        do
        {
            var remainingAttackers = attackers & *(occupancyBitBoards + turn) & occupancy;
            var sidePieces = turn == 0 ? whitePieces : blackPieces;
            var pinLine = turn == 0 ? pinLineWhite : pinLineBlack;
            var pinned = turn == 0 ? pinnedWhite : pinnedBlack;
            remainingAttackers &= sidePieces & ~(pinned & ~pinLine);

            // A king may not recapture onto a defended square. Other attackers
            // are dealt with by the pin filter above and the exchange sequence.
            var kingAttackers = remainingAttackers & kings;
            if (kingAttackers != 0 && IsSquareAttacked(targetSquare, turn ^ 1,
                    occupancy | (1UL << targetSquare), whitePieces, blackPieces,
                    pawns, knights, bishops, rooks, queens, kings))
                remainingAttackers &= ~kingAttackers;

            if (remainingAttackers == 0)
            {
                // No attacks left on the target square
                break;
            }

            *(captures + cIndex) = (short)(-*(captures + cIndex - 1) + lastPieceValue);

            // Remove the least valuable attacker
            ulong attacker;
            if ((attacker = pawns & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.PawnValue;
            }
            else if ((attacker = knights & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.KnightValue;
            }
            else if ((attacker = bishops & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.BishopValue;
            }
            else if ((attacker = rooks & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.RookValue;
            }
            else if ((attacker = queens & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.QueenValue;
            }
            else if ((attacker = kings & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.KingValue;
            }

            // Attacker is removed from occupancy
            int removedSquare = (int)System.Numerics.BitOperations.TrailingZeroCount(attacker);
            occupancy &= ~(1UL << removedSquare);
            if (turn == 0)
                whitePieces &= ~(1UL << removedSquare);
            else
                blackPieces &= ~(1UL << removedSquare);

            // Removing a piece can change a pin only when its square lies on a
            // king ray: it must have been a blocker or the pinning slider. Keep
            // the masks across exchange plies that cannot affect either king.
            var removedSquareBit = 1UL << removedSquare;
            if ((kingRayWhite & removedSquareBit) != 0)
            {
                var pinOccupancy = occupancy | (1UL << targetSquare);
                pinnedWhite = ComputePinned(whiteKingSquare, whitePieces,
                    blackPieces & (rooks | queens), blackPieces & (bishops | queens), pinOccupancy);
            }
            if ((kingRayBlack & removedSquareBit) != 0)
            {
                var pinOccupancy = occupancy | (1UL << targetSquare);
                pinnedBlack = ComputePinned(blackKingSquare, blackPieces,
                    whitePieces & (rooks | queens), whitePieces & (bishops | queens), pinOccupancy);
            }

            // Update attackers with any discovered attacks. A discovered slider attack on
            // the target can only appear when the removed square lay on a slider ray to it.
            if ((aligned & (1UL << removedSquare)) != 0)
            {
                attackers |= ((AttackTables.BishopAttacks(occupancy, targetSquare) & (bishops | queens)) |
                              (AttackTables.RookAttacks(occupancy, targetSquare) & (rooks | queens))) & occupancy;
            }

            // Flip turn
            turn ^= 1;
            cIndex++;
        } while (true);

        for (var n = cIndex - 1; n > 0; n--)
        {
            *(captures + n - 1) = (short)-Math.Max(-*(captures + n - 1), *(captures + n));
        }

        return *captures;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool IsSquareAttacked(int square, int bySide, ulong occupancy,
        ulong whitePieces, ulong blackPieces, ulong pawns, ulong knights,
        ulong bishops, ulong rooks, ulong queens, ulong kings)
    {
        var attackingPieces = bySide == 0 ? whitePieces : blackPieces;
        return (((AttackTables.BishopAttacks(occupancy, square) & (bishops | queens)) |
                 (AttackTables.RookAttacks(occupancy, square) & (rooks | queens)) |
                 (*(AttackTables.KnightAttackTable + square) & knights) |
                 (*(AttackTables.WhitePawnAttackTable + square) & (bySide == 1 ? pawns : 0)) |
                 (*(AttackTables.BlackPawnAttackTable + square) & (bySide == 0 ? pawns : 0)) |
                 (*(AttackTables.KingAttackTable + square) & kings)) & occupancy & attackingPieces) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe ulong ComputePinned(int kingSquare, ulong ownPieces, ulong opponentRooksQueens,
        ulong opponentBishopsQueens, ulong occupancy)
    {
        var pinned = 0UL;

        // Remove all friendly blockers to find the first opposing slider on each
        // ray, then use the between-squares table to identify a lone pinned piece.
        var rookPinners = AttackTables.RookAttacks(occupancy & ~ownPieces, kingSquare) & opponentRooksQueens;
        while (rookPinners != 0)
        {
            var pinnerSquare = (int)System.Numerics.BitOperations.TrailingZeroCount(rookPinners);
            rookPinners &= rookPinners - 1;
            var blockers = *(AttackTables.LineBitBoards + (kingSquare << 6) + pinnerSquare) & occupancy;
            if (blockers != 0 && (blockers & (blockers - 1)) == 0 && (blockers & ownPieces) != 0)
                pinned |= blockers;
        }

        var bishopPinners = AttackTables.BishopAttacks(occupancy & ~ownPieces, kingSquare) & opponentBishopsQueens;
        while (bishopPinners != 0)
        {
            var pinnerSquare = (int)System.Numerics.BitOperations.TrailingZeroCount(bishopPinners);
            bishopPinners &= bishopPinners - 1;
            var blockers = *(AttackTables.LineBitBoards + (kingSquare << 6) + pinnerSquare) & occupancy;
            if (blockers != 0 && (blockers & (blockers - 1)) == 0 && (blockers & ownPieces) != 0)
                pinned |= blockers;
        }

        return pinned;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong LineThrough(int squareA, int squareB)
    {
        var squareBBit = 1UL << squareB;
        if ((AttackTables.RookAttackMasksAll[squareA] & squareBBit) != 0)
        {
            return AttackTables.RookAttackMasksAll[squareA] & AttackTables.RookAttackMasksAll[squareB];
        }

        if ((AttackTables.BishopAttackMasksAll[squareA] & squareBBit) != 0)
        {
            return AttackTables.BishopAttackMasksAll[squareA] & AttackTables.BishopAttackMasksAll[squareB];
        }

        return 0;
    }
}
