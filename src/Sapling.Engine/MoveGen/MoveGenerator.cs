using System.Runtime.CompilerServices;

namespace Sapling.Engine.MoveGen;

public static class MoveGenerator
{
    public const ulong BlackKingSideCastleRookPosition = 1UL << 63;
    public const ulong BlackKingSideCastleEmptyPositions = (1UL << 61) | (1UL << 62);
    public const ulong BlackQueenSideCastleRookPosition = 1UL << 56;
    public const ulong BlackQueenSideCastleEmptyPositions = (1UL << 57) | (1UL << 58) | (1UL << 59);

    public const ulong WhiteKingSideCastleRookPosition = 1UL << 7;
    public const ulong WhiteKingSideCastleEmptyPositions = (1UL << 6) | (1UL << 5);
    public const ulong WhiteQueenSideCastleRookPosition = 1UL;
    public const ulong WhiteQueenSideCastleEmptyPositions = (1UL << 1) | (1UL << 2) | (1UL << 3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void GenerateLegalMoves(this ref BoardStateData board, List<uint> legalMoves, bool captureOnly)
    {
        legalMoves.Clear();
        var moves = stackalloc uint[218];
        var moveCount = board.GeneratePseudoLegalMoves(moves, captureOnly);

        BoardStateData copy = default;

        // Evaluate each position
        for (var moveIndex = 0; moveIndex < moveCount; ++moveIndex)
        {
            var m = moves[moveIndex];
            board.CloneTo(ref copy);
            if (copy.WhiteToMove ? copy.PartialApplyWhite(m) : copy.PartialApplyBlack(m))
            {
                legalMoves.Add(m);
            }
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe int GeneratePseudoLegalMoves(this ref BoardStateData board, uint* moves,
        bool captureOnly)
    {
        byte moveIndex = 0;

        if (board.WhiteToMove)
        {
            board.GenerateWhiteKingPseudoLegalMoves(moves, ref moveIndex, board.WhiteKingSquare,
                captureOnly);

            var positions = board.Occupancy[Constants.WhitePawn];
            while (positions != 0)
            {
                board.GetWhitePawnPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }

            positions = board.Occupancy[Constants.WhiteKnight];
            while (positions != 0)
            {
                board.GetWhiteKnightPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }

            positions = board.Occupancy[Constants.WhiteBishop];
            while (positions != 0)
            {
                board.GetWhiteBishopPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }

            positions = board.Occupancy[Constants.WhiteRook];
            while (positions != 0)
            {
                board.GetWhiteRookPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }

            positions = board.Occupancy[Constants.WhiteQueen];
            while (positions != 0)
            {
                board.GetWhiteQueenPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }
        }
        else
        {
            board.GetBlackKingPseudoLegalMoves(moves, ref moveIndex, board.BlackKingSquare,
                captureOnly);

            var positions = board.Occupancy[Constants.BlackPawn];
            while (positions != 0)
            {
                board.GetBlackPawnPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }

            positions = board.Occupancy[Constants.BlackKnight];
            while (positions != 0)
            {
                board.GetBlackKnightPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }

            positions = board.Occupancy[Constants.BlackBishop];
            while (positions != 0)
            {
                board.GetBlackBishopPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }

            positions = board.Occupancy[Constants.BlackRook];
            while (positions != 0)
            {
                board.GetBlackRookPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }

            positions = board.Occupancy[Constants.BlackQueen];
            while (positions != 0)
            {
                board.GetBlackQueenPseudoLegalMoves(moves, ref moveIndex, positions.PopLSB(),
                    captureOnly);
            }
        }

        return moveIndex;
    }


    #region Pawn
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetBlackPawnPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var rankIndex = index.GetRankIndex();
        var posEncoded = 1UL << index;

        if (board.EnPassantFile.CanEnPassant() && !board.WhiteToMove && rankIndex.IsBlackEnPassantRankIndex() &&
            Math.Abs(index.GetFileIndex() - board.EnPassantFile) == 1)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeBlackEnpassantMove(index, board.EnPassantFile);
        }

        var canPromote = rankIndex.IsSecondRank();
        byte toSquare;

        // Left capture
        var target = posEncoded.ShiftDownLeft();
        if ((board.Occupancy[Constants.WhitePieces] & target) != 0)
        {
            toSquare = index.ShiftDownLeft();
            var capturePiece = board.GetWhitePiece(toSquare);

            if (canPromote)
            {
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnKnightPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnBishopPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnRookPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnQueenPromotion);
            }
            else
            {
                *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare);
            }
        }

        // Right capture
        target = posEncoded.ShiftDownRight();
        if ((board.Occupancy[Constants.WhitePieces] & target) != 0)
        {
            toSquare = index.ShiftDownRight();
            var capturePiece = board.GetWhitePiece(toSquare);

            if (canPromote)
            {
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnKnightPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnBishopPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnRookPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnQueenPromotion);
            }
            else
            {
                *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.BlackPawn, index,
                    capturePiece,
                    toSquare);
            }
        }

        // Vertical moves
        target = posEncoded.ShiftDown();
        if (board.IsSquareOccupied(target))
        {
            // Blocked from moving down
            return;
        }

        toSquare = index.ShiftDown();
        if (canPromote)
        {
            // Promotion
            *(moves + moveIndex++) = MoveExtensions.EncodePromotionMove(Constants.BlackPawn, index,
                toSquare,
                Constants.PawnKnightPromotion);
            *(moves + moveIndex++) = MoveExtensions.EncodePromotionMove(Constants.BlackPawn, index,
                toSquare,
                Constants.PawnBishopPromotion);
            *(moves + moveIndex++) = MoveExtensions.EncodePromotionMove(Constants.BlackPawn, index,
                toSquare,
                Constants.PawnRookPromotion);
            *(moves + moveIndex++) = MoveExtensions.EncodePromotionMove(Constants.BlackPawn, index,
                toSquare,
                Constants.PawnQueenPromotion);
            return;
        }

        if (captureOnly)
        {
            // Only generating capture / promotion moves
            return;
        }

        // Move down
        *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.BlackPawn, index, toSquare);

        target = target.ShiftDown();
        if (rankIndex.IsSeventhRank() && board.IsEmptySquare(target))
        {
            // Double push
            *(moves + moveIndex++) = MoveExtensions.EncodeBlackDoublePushMove(index, toSquare.ShiftDown());
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetWhitePawnPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var rankIndex = index.GetRankIndex();
        var posEncoded = 1UL << index;

        if (board.EnPassantFile.CanEnPassant() && board.WhiteToMove && rankIndex.IsWhiteEnPassantRankIndex() &&
            Math.Abs(index.GetFileIndex() - board.EnPassantFile) == 1)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeWhiteEnpassantMove(index, board.EnPassantFile);
        }

        var canPromote = rankIndex.IsSeventhRank();
        byte toSquare;

        // Take left piece
        var target = posEncoded.ShiftUpLeft();
        if ((board.Occupancy[Constants.BlackPieces] & target) != 0)
        {
            toSquare = index.ShiftUpLeft();
            var capturePiece = board.GetBlackPiece(toSquare);

            if (canPromote)
            {
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnKnightPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnBishopPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnRookPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnQueenPromotion);
            }
            else
            {
                *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare);
            }
        }

        target = posEncoded.ShiftUpRight();
        // Take right piece
        if ((board.Occupancy[Constants.BlackPieces] & target) != 0)
        {
            toSquare = index.ShiftUpRight();
            var capturePiece = board.GetBlackPiece(toSquare);

            if (canPromote)
            {
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnKnightPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnBishopPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnRookPromotion);
                *(moves + moveIndex++) = MoveExtensions.EncodeCapturePromotionMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare,
                    Constants.PawnQueenPromotion);
            }
            else
            {
                *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.WhitePawn, index,
                    capturePiece,
                    toSquare);
            }
        }

        // Move up
        target = posEncoded.ShiftUp();
        if (board.IsSquareOccupied(target))
        {
            return;
        }

        toSquare = index.ShiftUp();
        if (canPromote)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodePromotionMove(Constants.WhitePawn, index,
                toSquare,
                Constants.PawnKnightPromotion);
            *(moves + moveIndex++) = MoveExtensions.EncodePromotionMove(Constants.WhitePawn, index,
                toSquare,
                Constants.PawnBishopPromotion);
            *(moves + moveIndex++) = MoveExtensions.EncodePromotionMove(Constants.WhitePawn, index,
                toSquare,
                Constants.PawnRookPromotion);
            *(moves + moveIndex++) = MoveExtensions.EncodePromotionMove(Constants.WhitePawn, index,
                toSquare,
                Constants.PawnQueenPromotion);
            return;
        }

        if (captureOnly)
        {
            return;
        }

        *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.WhitePawn, index, toSquare);

        target = target.ShiftUp();
        if (rankIndex.IsSecondRank() && board.IsEmptySquare(target))
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeWhiteDoublePushMove(index, toSquare.ShiftUp());
        }
    }

    #endregion

    #region Knight
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetWhiteKnightPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = *(AttackTables.KnightAttackTable + index);
        var captureMoves = potentialMoves & board.Occupancy[Constants.BlackPieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.WhiteKnight, index,
                board.GetBlackPiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.WhiteKnight, index,
                emptyMoves.PopLSB());
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetBlackKnightPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = *(AttackTables.KnightAttackTable + index);
        var captureMoves = potentialMoves & board.Occupancy[Constants.WhitePieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.BlackKnight, index,
                board.GetWhitePiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.BlackKnight, index,
                emptyMoves.PopLSB());
        }
    }

    #endregion

    #region Rook
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetWhiteRookPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = AttackTables.PextRookAttacks(board.Occupancy[Constants.Occupancy], index);
        var captureMoves = potentialMoves & board.Occupancy[Constants.BlackPieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.WhiteRook, index,
                board.GetBlackPiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.WhiteRook, index, emptyMoves.PopLSB());
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetBlackRookPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = AttackTables.PextRookAttacks(board.Occupancy[Constants.Occupancy], index);
        var captureMoves = potentialMoves & board.Occupancy[Constants.WhitePieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.BlackRook, index,
                board.GetWhitePiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.BlackRook, index, emptyMoves.PopLSB());
        }
    }

    #endregion

    #region Bishop
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetWhiteBishopPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = AttackTables.PextBishopAttacks(board.Occupancy[Constants.Occupancy], index);
        var captureMoves = potentialMoves & board.Occupancy[Constants.BlackPieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.WhiteBishop, index,
                board.GetBlackPiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.WhiteBishop, index, emptyMoves.PopLSB());
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetBlackBishopPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = AttackTables.PextBishopAttacks(board.Occupancy[Constants.Occupancy], index);
        var captureMoves = potentialMoves & board.Occupancy[Constants.WhitePieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.BlackBishop, index,
                board.GetWhitePiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.BlackBishop, index, emptyMoves.PopLSB());
        }
    }

    #endregion

    #region Queen
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetWhiteQueenPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = AttackTables.PextBishopAttacks(board.Occupancy[Constants.Occupancy], index) |
                             AttackTables.PextRookAttacks(board.Occupancy[Constants.Occupancy], index);
        var captureMoves = potentialMoves & board.Occupancy[Constants.BlackPieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.WhiteQueen, index,
                board.GetBlackPiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.WhiteQueen, index, emptyMoves.PopLSB());
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetBlackQueenPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = AttackTables.PextBishopAttacks(board.Occupancy[Constants.Occupancy], index) |
                             AttackTables.PextRookAttacks(board.Occupancy[Constants.Occupancy], index);
        var captureMoves = potentialMoves & board.Occupancy[Constants.WhitePieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.BlackQueen, index,
                board.GetWhitePiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.BlackQueen, index, emptyMoves.PopLSB());
        }
    }

    #endregion

    #region King
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void GenerateWhiteKingPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = *(AttackTables.KingAttackTable + index);

        var captureMoves = potentialMoves & board.Occupancy[Constants.BlackPieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.WhiteKing, index,
                board.GetBlackPiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.WhiteKing, index,
                emptyMoves.PopLSB());
        }

        if (board.InCheck)
        {
            // Can't castle if king is attacked or not on the starting position
            return;
        }

        // King Side Castle
        if ((board.CastleRights & CastleRights.WhiteKingSide) != 0)
        {
            var rookSquare = board.Is960 ? board.WhiteKingSideTargetSquare : 7;

            var startSquare = Math.Min(board.WhiteKingSquare, Math.Min(rookSquare, 5));
            var endSquare = Math.Max(board.WhiteKingSquare, Math.Max(rookSquare, 6));

            ulong path = (*(AttackTables.LineBitBoardsInclusive + (startSquare << 6) + endSquare) & board.Occupancy[Constants.Occupancy]) & ~((1UL << rookSquare) | (1UL << board.WhiteKingSquare));
            if (path == 0)
            {
                var canCastle = true;
                var kingStart = Math.Min((int)board.WhiteKingSquare, 6);
                var kingEnd = Math.Max((int)board.WhiteKingSquare, 6);
                for (var i = kingEnd; i >= kingStart; i--)
                {
                    if (board.IsAttackedByBlack(i))
                    {
                        canCastle = false;
                        break;
                    }
                }

                if (canCastle)
                {
                    *(moves + moveIndex++) = MoveExtensions.EncodeCastleMove(Constants.WhiteKing, index,
                        board.WhiteKingSideTargetSquare);
                }
            }
        }

        // Queen Side Castle
        if ((board.CastleRights & CastleRights.WhiteQueenSide) != 0)
        {
            var rookSquare = board.Is960 ? board.WhiteQueenSideTargetSquare : 0;
            var startSquare = Math.Min(board.WhiteKingSquare, Math.Min(rookSquare, 2));
            var endSquare = Math.Max(board.WhiteKingSquare, Math.Max(rookSquare, 3));
            ulong path = (*(AttackTables.LineBitBoardsInclusive + (startSquare << 6) + endSquare) & board.Occupancy[Constants.Occupancy]) & ~((1UL << rookSquare) | (1UL << board.WhiteKingSquare));

            if (path == 0)
            {
                bool canCastle = true;
                var kingStart = Math.Min((int)board.WhiteKingSquare, 2);
                var kingEnd = Math.Max((int)board.WhiteKingSquare, 2);
                for (var i = kingEnd; i >= kingStart; i--)
                {
                    if (board.IsAttackedByBlack(i))
                    {
                        canCastle = false;
                        break;
                    }
                }

                if (canCastle)
                {
                    *(moves + moveIndex++) = MoveExtensions.EncodeCastleMove(Constants.WhiteKing, index,
                        board.WhiteQueenSideTargetSquare);
                }
            }
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static unsafe void GetBlackKingPseudoLegalMoves(this ref BoardStateData board, uint* moves,
        ref byte moveIndex,
        byte index, bool captureOnly)
    {
        var potentialMoves = *(AttackTables.KingAttackTable + index);
        var captureMoves = potentialMoves & board.Occupancy[Constants.WhitePieces];
        while (captureMoves != 0)
        {
            var i = captureMoves.PopLSB();
            *(moves + moveIndex++) = MoveExtensions.EncodeCaptureMove(Constants.BlackKing, index,
                board.GetWhitePiece(i), i);
        }

        if (captureOnly)
        {
            return;
        }

        var emptyMoves = potentialMoves & ~board.Occupancy[Constants.Occupancy];
        while (emptyMoves != 0)
        {
            *(moves + moveIndex++) = MoveExtensions.EncodeNormalMove(Constants.BlackKing, index,
                emptyMoves.PopLSB());
        }

        if (board.InCheck)
        {
            // Can't castle if king is attacked or not on the starting position
            return;
        }
        
        // King Side Castle
        if ((board.CastleRights & CastleRights.BlackKingSide) != 0)
        {
            var rookSquare = board.Is960 ? board.BlackKingSideTargetSquare : 63;

            var startSquare = Math.Min(board.BlackKingSquare, Math.Min(rookSquare, 61));
            var endSquare = Math.Max(board.BlackKingSquare, Math.Max(rookSquare, 62));

            ulong path = (*(AttackTables.LineBitBoardsInclusive + (startSquare << 6) + endSquare) & board.Occupancy[Constants.Occupancy]) & ~((1UL << rookSquare) | (1UL << board.BlackKingSquare));

            if (path == 0)
            {
                bool canCastle = true;
                var kingStart = Math.Min((int)board.BlackKingSquare, 62);
                var kingEnd = Math.Max((int)board.BlackKingSquare, 62);

                for (var i = kingEnd; i >= kingStart; i--)
                {
                    if (board.IsAttackedByWhite(i))
                    {
                        canCastle = false;
                        break;
                    }
                }

                if (canCastle)
                {
                    *(moves + moveIndex++) = MoveExtensions.EncodeCastleMove(Constants.BlackKing, index,
                        board.BlackKingSideTargetSquare);
                }
            }
        }

        // Queen Side Castle
        if ((board.CastleRights & CastleRights.BlackQueenSide) != 0)
        {
            var rookSquare = board.Is960 ? board.BlackQueenSideTargetSquare : 56;
            var startSquare = Math.Min(board.BlackKingSquare, Math.Min(rookSquare, 58));
            var endSquare = Math.Max(board.BlackKingSquare, Math.Max(rookSquare, 59));

            ulong path = (*(AttackTables.LineBitBoardsInclusive + (startSquare << 6) + endSquare) & board.Occupancy[Constants.Occupancy]) & ~((1UL << rookSquare) | (1UL << board.BlackKingSquare));

            if (path == 0)
            {
                bool canCastle = true;
                var kingStart = Math.Min((int)board.BlackKingSquare, 58);
                var kingEnd = Math.Max((int)board.BlackKingSquare, 58);
                for (var i = kingEnd; i >= kingStart; i--)
                {
                    if (board.IsAttackedByWhite(i))
                    {
                        canCastle = false;
                        break;
                    }
                }

                if (canCastle)
                {
                    *(moves + moveIndex++) = MoveExtensions.EncodeCastleMove(Constants.BlackKing, index,
                        board.BlackQueenSideTargetSquare);
                }
            }
        }
    }

    #endregion
}