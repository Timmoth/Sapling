using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Pgn;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.Search;

namespace Sapling.Engine;

public static class BoardStateExtensions
{
    public const uint BoardStateSize = 140;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanEnPassant(this byte enpassantFile)
    {
        return enpassantFile < 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool InsufficientMatingMaterial(this ref BoardStateData board)
    {
        if (board.PieceCount > 4)
        {
            return false;
        }

        var black = board.Occupancy[Constants.BlackPieces] & ~board.Occupancy[Constants.BlackKing];
        var white = board.Occupancy[Constants.WhitePieces] & ~board.Occupancy[Constants.WhiteKing];

        var blackPopCount = BitboardHelpers.PopCount(black);

        if ((white == 0 && black == board.Occupancy[Constants.BlackKnight] && blackPopCount <= 2))
        {
            // Can't mate with only knights
            return true;
        }

        var whitePopCount = BitboardHelpers.PopCount(white);
        if ((black == 0 && white == board.Occupancy[Constants.WhiteKnight] && whitePopCount <= 2))
        {
            // Can't mate with only knights
            return true;
        }

        // Can't mate with 1 knight and bishop
        return (black == 0 || (blackPopCount == 1 && (board.Occupancy[Constants.BlackKnight] | board.Occupancy[Constants.BlackBishop]) == black)) &&
               (white == 0 || (whitePopCount == 1 && (board.Occupancy[Constants.WhiteKnight] | board.Occupancy[Constants.WhiteBishop]) == white));
    }

    public static unsafe BoardStateData CreateBoardFromFen(string fen)
    {
        BoardStateData boardState = default;
        var parts = fen.Split(' ');

        var rows = parts[0].Split('/');
        var turn = parts[1];
        var castleRights = parts[2];
        var enPassantTarget = parts[3];

        ushort halfMoveClock = parts.Length > 4 ? ushort.Parse(parts[4]) : (ushort)0;
        ushort fullMoveNumber = parts.Length > 5 ? ushort.Parse(parts[5]) : (ushort)1;

        var index = 0;
        for (var i = rows.Length - 1; i >= 0; i--)
        {
            var row = rows[i];
            foreach (var c in row)
            {
                if (char.IsDigit(c))
                {
                    index += (int)char.GetNumericValue(c);
                }
                else
                {
                    var piece = c.CharToPiece();

                    if (piece == Constants.None)
                    {
                        continue;
                    }

                    boardState.Set((byte)piece, (byte)index);
                    index++;
                }
            }
        }

        boardState.CastleRights = CastleRights.None;
        if (castleRights.Contains("K"))
        {
            boardState.CastleRights |= CastleRights.WhiteKingSide;
        }

        if (castleRights.Contains("Q"))
        {
            boardState.CastleRights |= CastleRights.WhiteQueenSide;
        }

        if (castleRights.Contains("k"))
        {
            boardState.CastleRights |= CastleRights.BlackKingSide;
        }

        if (castleRights.Contains("q"))
        {
            boardState.CastleRights |= CastleRights.BlackQueenSide;
        }

        if (enPassantTarget == "-")
        {
            boardState.EnPassantFile = 8;
        }
        else
        {
            var (file, _) = enPassantTarget.GetPosition();
            boardState.EnPassantFile = (byte)file;
        }

        boardState.TurnCount = fullMoveNumber;
        boardState.HalfMoveClock = (byte)halfMoveClock;
        boardState.WhiteToMove = turn == "w";

        boardState.Occupancy[Constants.Occupancy] = boardState.Occupancy[Constants.WhitePieces] | boardState.Occupancy[Constants.BlackPieces];
        boardState.Hash = Zobrist.CalculateZobristKey(ref boardState);

        boardState.UpdateCheckStatus();

        return boardState;
    }

    public static unsafe void ResetToFen(this ref BoardStateData boardState, string fen)
    {
        var parts = fen.Split(' ');

        var rows = parts[0].Split('/');
        var turn = parts[1];
        var castleRights = parts[2];
        var enPassantTarget = parts[3];

        ushort halfMoveClock = parts.Length > 4 ? ushort.Parse(parts[4]) : (ushort)0;
        ushort fullMoveNumber = parts.Length > 5 ? ushort.Parse(parts[5]) : (ushort)1;

        var index = 0;
        for (var i = rows.Length - 1; i >= 0; i--)
        {
            var row = rows[i];
            foreach (var c in row)
            {
                if (char.IsDigit(c))
                {
                    index += (int)char.GetNumericValue(c);
                }
                else
                {
                    var piece = c.CharToPiece();

                    if (piece == Constants.None)
                    {
                        continue;
                    }

                    boardState.Set((byte)piece, (byte)index);
                    index++;
                }
            }
        }

        boardState.CastleRights = CastleRights.None;
        if (castleRights.Contains("K"))
        {
            boardState.CastleRights |= CastleRights.WhiteKingSide;
        }

        if (castleRights.Contains("Q"))
        {
            boardState.CastleRights |= CastleRights.WhiteQueenSide;
        }

        if (castleRights.Contains("k"))
        {
            boardState.CastleRights |= CastleRights.BlackKingSide;
        }

        if (castleRights.Contains("q"))
        {
            boardState.CastleRights |= CastleRights.BlackQueenSide;
        }

        if (enPassantTarget == "-")
        {
            boardState.EnPassantFile = 8;
        }
        else
        {
            var (file, _) = enPassantTarget.GetPosition();
            boardState.EnPassantFile = (byte)file;
        }

        boardState.TurnCount = fullMoveNumber;
        boardState.HalfMoveClock = (byte)halfMoveClock;
        boardState.WhiteToMove = turn == "w";

        boardState.Occupancy[Constants.Occupancy] = boardState.Occupancy[Constants.WhitePieces] | boardState.Occupancy[Constants.BlackPieces];
        boardState.Hash = Zobrist.CalculateZobristKey(ref boardState);

        boardState.UpdateCheckStatus();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void WhiteKingCastle(ulong* occupancy)
    {
        var kingOccupancy = occupancy + Constants.WhiteKing;
        *kingOccupancy = (*kingOccupancy & ~(1UL << 4)) | (1UL << 6);

        var rookOccupancy = occupancy + Constants.WhiteRook;
        *rookOccupancy = (*rookOccupancy & ~(1UL << 7)) | (1UL << 5);

        var occupancyPtr = occupancy + Constants.WhitePieces;
        *(occupancyPtr) = (*(occupancyPtr) & ~(1UL << 4 | 1UL << 7)) | (1UL << 6 | 1UL << 5);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void WhiteQueenCastle(ulong* occupancy)
    {
        var kingOccupancy = occupancy + Constants.WhiteKing;
        *kingOccupancy = (*kingOccupancy & ~(1UL << 4)) | (1UL << 2);

        var rookOccupancy = occupancy + Constants.WhiteRook;
        *rookOccupancy = (*rookOccupancy & ~(1UL << 0)) | (1UL << 3);

        var occupancyPtr = occupancy + Constants.WhitePieces;
        *(occupancyPtr) = (*(occupancyPtr) & ~(1UL << 4 | 1UL << 0)) | (1UL << 2 | 1UL << 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void BlackKingCastle(ulong* occupancy)
    {
        var kingOccupancy = occupancy + Constants.BlackKing;
        *kingOccupancy = (*kingOccupancy & ~(1UL << 60)) | (1UL << 62);

        var rookOccupancy = occupancy + Constants.BlackRook;
        *rookOccupancy = (*rookOccupancy & ~(1UL << 63)) | (1UL << 61);

        var occupancyPtr = occupancy + Constants.BlackPieces;
        *(occupancyPtr) = (*(occupancyPtr) & ~(1UL << 60 | 1UL << 63)) | (1UL << 62 | 1UL << 61);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void BlackQueenCastle(ulong* occupancy)
    {
        var kingOccupancy = occupancy + Constants.BlackKing;
        *kingOccupancy = (*kingOccupancy & ~(1UL << 60)) | (1UL << 58);

        var rookOccupancy = occupancy + Constants.BlackRook;
        *rookOccupancy = (*rookOccupancy & ~(1UL << 56)) | (1UL << 59);

        var occupancyPtr = occupancy + Constants.BlackPieces;
        *(occupancyPtr) = (*(occupancyPtr) & ~(1UL << 60 | 1UL << 56)) | (1UL << 58 | 1UL << 59);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Move(ulong* occupancy, byte piece, byte fromSquare, byte toSquare)
    {
        var fromMask = 1UL << fromSquare;
        var toMask = 1UL << toSquare;

        var pieceOccupancy = occupancy + piece;
        var sideOccupancy = occupancy + Constants.WhitePieces + (piece & 1);

        *(pieceOccupancy) = (*(pieceOccupancy) & ~fromMask) | toMask;
        *(sideOccupancy) = (*(sideOccupancy) & ~fromMask) | toMask;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Add(ulong* occupancy, byte piece, byte square)
    {
        var pos = 1UL << square;
        *(occupancy + piece) |= pos;
        *(occupancy + Constants.WhitePieces + (piece & 1)) |= pos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Remove(ulong* occupancy, byte piece, byte square)
    {
        var pos = ~(1UL << square);
        *(occupancy + piece) &= pos;
        *(occupancy + Constants.WhitePieces + (piece & 1)) &= pos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool PartialApply(this ref BoardStateData board, uint m)
    {
        m.DeconstructByte(out var movedPiece, out var fromSquare, out var toSquare, out var capturedPiece, out var moveType);
        fixed (ulong* oc = board.Occupancy)
        {
            if (moveType == 0)
            {
                // Normal move
                Move(oc, movedPiece, fromSquare, toSquare);
                board.EnPassantFile = 8;
                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;
                    Remove(oc, capturedPiece, toSquare);
                }

                if (movedPiece == Constants.WhiteKing)
                {
                    board.WhiteKingSquare = toSquare;
                    board.CastleRights &= ~Constants.WhiteCastleRights;
                }
                else if (movedPiece == Constants.BlackKing)
                {
                    board.BlackKingSquare = toSquare;
                    board.CastleRights &= ~Constants.BlackCastleRights;
                }
                else if (movedPiece == Constants.WhiteRook)
                {
                    if (fromSquare == 4)
                    {
                        board.CastleRights &= ~CastleRights.WhiteQueenSide;
                    }
                    else if (fromSquare == 7)
                    {
                        board.CastleRights &= ~CastleRights.WhiteKingSide;
                    }
                }
                else if (movedPiece == Constants.BlackRook)
                {
                    if (fromSquare == 56)
                    {
                        board.CastleRights &= ~CastleRights.BlackQueenSide;
                    }
                    else if (fromSquare == 63)
                    {
                        board.CastleRights &= ~CastleRights.BlackKingSide;
                    }
                }
            }
            else if (moveType == Constants.DoublePush)
            {
                Move(oc, movedPiece, fromSquare, toSquare);

                board.EnPassantFile = (byte)(fromSquare % 8);
            }
            else if (moveType == Constants.Castle)
            {
                // Castle move
                if (toSquare == 62)
                {
                    BlackKingCastle(oc);
                    board.BlackKingSquare = toSquare;
                    board.CastleRights &= ~Constants.BlackCastleRights;
                }
                else if (toSquare == 58)
                {
                    BlackQueenCastle(oc);
                    board.BlackKingSquare = toSquare;
                    board.CastleRights &= ~Constants.BlackCastleRights;
                }
                else if (toSquare == 6)
                {
                    WhiteKingCastle(oc);
                    board.WhiteKingSquare = toSquare;
                    board.CastleRights &= ~Constants.WhiteCastleRights;
                }
                else if (toSquare == 2)
                {
                    WhiteQueenCastle(oc);
                    board.WhiteKingSquare = toSquare;
                    board.CastleRights &= ~Constants.WhiteCastleRights;
                }
                board.EnPassantFile = 8; // Reset
            }

            else if (moveType >= 4)
            {
                // Pawn Promotion
                // [pawn, moveType] => piece
                // [1, 4] => 3
                // [2, 4] => 4
                // [1, 5] => 5
                // [2, 5] => 6
                // [1, 6] => 7
                // [2, 6] => 8
                // [1, 7] => 9
                // [2, 7] => 10
                // a + 2b - 6
                var promotionPiece = (byte)(movedPiece + moveType + moveType - 6);
                Add(oc, promotionPiece, toSquare);
                Remove(oc, movedPiece, fromSquare);
                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;
                    Remove(oc, capturedPiece, toSquare);
                }

                board.EnPassantFile = 8;
            }
            else
            {
                // Enpassant
                Move(oc, movedPiece, fromSquare, toSquare);

                var enpassantSquare = (byte)(fromSquare.GetRankIndex() * 8 + board.EnPassantFile);
                Remove(oc, capturedPiece, enpassantSquare);

                // Clear enpassant file
                board.EnPassantFile = 8;
                --board.PieceCount;
            }


            if (m.IsReset())
            {
                board.HalfMoveClock = 0;
            }
            else
            {
                board.HalfMoveClock++;
            }

            board.TurnCount++;
            board.WhiteToMove = !board.WhiteToMove;
            *(oc + Constants.Occupancy) = *(oc + Constants.WhitePieces) | *(oc + Constants.BlackPieces);
        }

        return (!board.WhiteToMove && !board.IsAttackedByBlack(board.WhiteKingSquare)) ||
               (board.WhiteToMove && !board.IsAttackedByWhite(board.BlackKingSquare));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateCheckStatus(this ref BoardStateData board)
    {
        board.InCheck = board.WhiteToMove ? board.IsAttackedByBlack(board.WhiteKingSquare) : board.IsAttackedByWhite(board.BlackKingSquare);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FinishApply(this ref BoardStateData board, ref AccumulatorState accumulatorState, uint m,
        int oldEnpassant,
        CastleRights prevCastle)
    {
        accumulatorState.Move = m;
        ref var hash = ref board.Hash;
        hash ^= Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));

        m.Deconstruct(out var movedPiece, out var fromSquare, out var toSquare, out var capturedPiece, out var moveType);

        var movedPieceZobristIndex = (movedPiece << 6);
        var movedPieceFeatureIndex = (movedPiece << 7);
        if (moveType == 0)
        {
            hash ^= *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                    *(Zobrist.PiecesArray + movedPieceZobristIndex + toSquare);

            if (capturedPiece != Constants.None)
            {
                accumulatorState.ApplyCapture(movedPieceFeatureIndex + (fromSquare << 1),
                    movedPieceFeatureIndex + (toSquare << 1),
                    (capturedPiece << 7) + (toSquare << 1));
                hash ^= *(Zobrist.PiecesArray + (capturedPiece << 6) + toSquare);
            }
            else
            {
                accumulatorState.ApplyQuiet(movedPieceFeatureIndex + (fromSquare << 1),
                    movedPieceFeatureIndex + (toSquare << 1));
            }
        }
        else if (moveType == Constants.DoublePush)
        {
            accumulatorState.ApplyQuiet(movedPieceFeatureIndex + (fromSquare << 1),
                movedPieceFeatureIndex + (toSquare << 1));
            hash ^= *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                    *(Zobrist.PiecesArray + movedPieceZobristIndex + toSquare);
        }
        else if (moveType == Constants.Castle)
        {
            // Castle move
            if (toSquare == 62)
            {
                accumulatorState.ApplyCastle(Constants.BlackKingSideCastleKingFromIndex,
                    Constants.BlackKingSideCastleKingToIndex,
                    Constants.BlackKingSideCastleRookFromIndex,
                    Constants.BlackKingSideCastleRookToIndex);
                hash ^= Zobrist.BlackKingSideCastleZobrist;
            }
            else if (toSquare == 58)
            {
                accumulatorState.ApplyCastle(Constants.BlackQueenSideCastleKingFromIndex,
                    Constants.BlackQueenSideCastleKingToIndex,
                    Constants.BlackQueenSideCastleRookFromIndex,
                    Constants.BlackQueenSideCastleRookToIndex);
                hash ^= Zobrist.BlackQueenSideCastleZobrist;
            }
            else if (toSquare == 6)
            {
                accumulatorState.ApplyCastle(Constants.WhiteKingSideCastleKingFromIndex,
                    Constants.WhiteKingSideCastleKingToIndex,
                    Constants.WhiteKingSideCastleRookFromIndex,
                    Constants.WhiteKingSideCastleRookToIndex);
                hash ^= Zobrist.WhiteKingSideCastleZobrist;
            }
            else if (toSquare == 2)
            {
                accumulatorState.ApplyCastle(Constants.WhiteQueenSideCastleKingFromIndex,
                    Constants.WhiteQueenSideCastleKingToIndex,
                    Constants.WhiteQueenSideCastleRookFromIndex,
                    Constants.WhiteQueenSideCastleRookToIndex);
                hash ^= Zobrist.WhiteQueenSideCastleZobrist;
            }
        }
        else if (moveType >= 4)
        {
            // Pawn Promotion
            // [pawn, moveType] => piece
            // [1, 4] => 3
            // [2, 4] => 4
            // [1, 5] => 5
            // [2, 5] => 6
            // [1, 6] => 7
            // [2, 6] => 8
            // [1, 7] => 9
            // [2, 7] => 10
            // a + 2b - 6
            var promotionPiece = (ushort)(movedPiece + moveType + moveType - 6);
            hash ^= *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                      *(Zobrist.PiecesArray + (promotionPiece << 6) + toSquare);

            if (capturedPiece != Constants.None)
            {
                accumulatorState.ApplyCapture(movedPieceFeatureIndex + (fromSquare << 1),
                    (promotionPiece << 7) + (toSquare << 1),
                    (capturedPiece << 7) + (toSquare << 1));
                hash ^= *(Zobrist.PiecesArray + (capturedPiece << 6) + toSquare);
            }
            else
            {
                accumulatorState.ApplyQuiet(movedPieceFeatureIndex + (fromSquare << 1),
                    (promotionPiece << 7) + (toSquare << 1));
            }
        }
        else
        {
            // Enpassant
            var enpassantSquare = (ushort)(fromSquare.GetRankIndex() * 8 + oldEnpassant);
            accumulatorState.ApplyCapture(movedPieceFeatureIndex + (fromSquare << 1),
                movedPieceFeatureIndex + (toSquare << 1),
                (capturedPiece << 7) + (enpassantSquare << 1));

            hash ^= *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                      *(Zobrist.PiecesArray + movedPieceZobristIndex + toSquare) ^
                        *(Zobrist.PiecesArray + (capturedPiece << 6) + enpassantSquare);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ApplyNullMove(this ref BoardStateData board)
    {
        board.Hash ^= Zobrist.SideToMove;

        if (board.EnPassantFile != 8)
        {
            board.Hash ^= *(Zobrist.EnPassantFile + board.EnPassantFile);
            board.EnPassantFile = 8;
        }

        board.TurnCount++;
        board.WhiteToMove = !board.WhiteToMove;
        board.InCheck = false;
        board.HalfMoveClock = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Set(this ref BoardStateData board, byte piece, byte index)
    {
        board.PieceCount++;
        fixed (ulong* oc = board.Occupancy)
        {
            Add(oc, piece, index);
        }
        if (piece == Constants.WhiteKing)
        {
            board.WhiteKingSquare = index;
        }
        else if (piece == Constants.BlackKing)
        {
            board.BlackKingSquare = index;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool IsSquareOccupied(this ref BoardStateData board, ulong position)
    {
        return (board.Occupancy[Constants.Occupancy] & position) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool IsEmptySquare(this ref BoardStateData board, ulong position)
    {
        return (board.Occupancy[Constants.Occupancy] & position) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool HasMajorPieces(this ref BoardStateData board)
    {
        if (board.WhiteToMove)
        {
            return (board.Occupancy[Constants.WhitePieces] & ~(board.Occupancy[Constants.WhitePawn] | board.Occupancy[Constants.WhiteKing])) > 0;
        }

        return (board.Occupancy[Constants.BlackPieces] & ~(board.Occupancy[Constants.BlackPawn] | board.Occupancy[Constants.BlackKing])) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe byte GetPiece(this ref BoardStateData board, int square)
    {
        if ((board.Occupancy[Constants.Occupancy] & (1UL << square)) == 0)
        {
            return 0;
        }

        return (byte)Bmi1.X64.TrailingZeroCount(
                          (board.Occupancy[Constants.BlackPawn] >> square & 1UL) << 1 |
                          (board.Occupancy[Constants.WhitePawn] >> square & 1UL) << 2 |
                          (board.Occupancy[Constants.BlackKnight] >> square & 1UL) << 3 |
                          (board.Occupancy[Constants.WhiteKnight] >> square & 1UL) << 4 |
                          (board.Occupancy[Constants.BlackBishop] >> square & 1UL) << 5 |
                          (board.Occupancy[Constants.WhiteBishop] >> square & 1UL) << 6 |
                          (board.Occupancy[Constants.BlackRook] >> square & 1UL) << 7 |
                          (board.Occupancy[Constants.WhiteRook] >> square & 1UL) << 8 |
                          (board.Occupancy[Constants.BlackQueen] >> square & 1UL) << 9 |
                          (board.Occupancy[Constants.WhiteQueen] >> square & 1UL) << 10 |
                          (board.Occupancy[Constants.BlackKing] >> square & 1UL) << 11 |
                      (board.Occupancy[Constants.WhiteKing] >> square & 1UL) << 12);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe byte GetWhitePiece(this ref BoardStateData board, byte square)
    {
        var squareBB = 1UL << square;

        if ((board.Occupancy[Constants.WhitePawn] & squareBB) != 0)
        {
            return Constants.WhitePawn;
        }

        if ((board.Occupancy[Constants.WhiteKnight] & squareBB) != 0)
        {
            return Constants.WhiteKnight;
        }

        if ((board.Occupancy[Constants.WhiteBishop] & squareBB) != 0)
        {
            return Constants.WhiteBishop;
        }

        if ((board.Occupancy[Constants.WhiteRook] & squareBB) != 0)
        {
            return Constants.WhiteRook;
        }

        if ((board.Occupancy[Constants.WhiteQueen] & squareBB) != 0)
        {
            return Constants.WhiteQueen;
        }

        if ((board.Occupancy[Constants.WhiteKing] & squareBB) != 0)
        {
            return Constants.WhiteKing;
        }

        return 0;
        //return (byte)Bmi1.X64.TrailingZeroCount((board.Occupancy[Constants.WhitePawn] >> square & 1UL) << 2 |
        //                                          (board.Occupancy[Constants.WhiteKnight] >> square & 1UL) << 4 |
        //                                          (board.Occupancy[Constants.WhiteBishop] >> square & 1UL) << 6 |
        //                                          (board.Occupancy[Constants.WhiteRook] >> square & 1UL) << 8 |
        //                                          (board.Occupancy[Constants.WhiteQueen] >> square & 1UL) << 10 |
        //                                          (board.Occupancy[Constants.WhiteKing] >> square & 1UL) << 12);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe byte GetBlackPiece(this ref BoardStateData board, byte square)
    {
        var squareBB = 1UL << square;

        if ((board.Occupancy[Constants.BlackPawn] & squareBB) != 0)
        {
            return Constants.BlackPawn;
        }

        if ((board.Occupancy[Constants.BlackKnight] & squareBB) != 0)
        {
            return Constants.BlackKnight;
        }

        if ((board.Occupancy[Constants.BlackBishop] & squareBB) != 0)
        {
            return Constants.BlackBishop;
        }

        if ((board.Occupancy[Constants.BlackRook] & squareBB) != 0)
        {
            return Constants.BlackRook;
        }

        if ((board.Occupancy[Constants.BlackQueen] & squareBB) != 0)
        {
            return Constants.BlackQueen;
        }

        if ((board.Occupancy[Constants.BlackKing] & squareBB) != 0)
        {
            return Constants.BlackKing;
        }

        return 0;

        //return (byte)Bmi1.X64.TrailingZeroCount((board.Occupancy[Constants.BlackPawn] >> square & 1UL) << 1 |
        //                                          (board.Occupancy[Constants.BlackKnight] >> square & 1UL) << 3 |
        //                                          (board.Occupancy[Constants.BlackBishop] >> square & 1UL) << 5 |
        //                                          (board.Occupancy[Constants.BlackRook] >> square & 1UL) << 7 |
        //                                          (board.Occupancy[Constants.BlackQueen] >> square & 1UL) << 9 |
        //                                          (board.Occupancy[Constants.BlackKing] >> square & 1UL) << 11);
    }
}