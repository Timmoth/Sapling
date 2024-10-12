using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Pgn;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.Search;

namespace Sapling.Engine;

public static class BoardStateExtensions
{

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

    public static void SetCastleRights(ref BoardStateData boardState, string castleRights)
    {
        boardState.CastleRights = CastleRights.None;
        boardState.Is960 = false;

        foreach (char right in castleRights)
        {
            if (char.IsUpper(right))
            {
                // Uppercase = White's castling rights
                if (right == 'K')
                {
                    // Standard White kingside castling (rook on file 7, 'h')
                    boardState.CastleRights |= CastleRights.WhiteKingSide;
                    boardState.WhiteKingSideTargetSquare = 6; // Default to 'h' file
                }
                else if (right == 'Q')
                {
                    // Standard White queenside castling (rook on file 0, 'a')
                    boardState.CastleRights |= CastleRights.WhiteQueenSide;
                    boardState.WhiteQueenSideTargetSquare = 2; // Default to 'a' file
                }
                else if (right >= 'A' && right <= 'H')
                {
                    boardState.Is960 = true;

                    // Shredder FEN format (White's rook is on the given file)
                    var rookFile = (byte)(right - 'A'); // Map 'A' to file 0, 'B' to 1, ..., 'H' to 7
                    if (rookFile > boardState.WhiteKingSquare) // Files E-H (4-7) are kingside castling
                    {
                        boardState.CastleRights |= CastleRights.WhiteKingSide;
                        boardState.WhiteKingSideTargetSquare = rookFile;
                    }
                    else // Files A-D (0-3) are queenside castling
                    {
                        boardState.CastleRights |= CastleRights.WhiteQueenSide;
                        boardState.WhiteQueenSideTargetSquare = rookFile;
                    }
                }
            }
            else if (char.IsLower(right))
            {
                // Lowercase = Black's castling rights
                if (right == 'k')
                {
                    // Standard Black kingside castling (rook on file 7, 'h')
                    boardState.CastleRights |= CastleRights.BlackKingSide;
                    boardState.BlackKingSideTargetSquare = 62; // Default to 'h' file
                }
                else if (right == 'q')
                {
                    // Standard Black queenside castling (rook on file 0, 'a')
                    boardState.CastleRights |= CastleRights.BlackQueenSide;
                    boardState.BlackQueenSideTargetSquare = 58; // Default to 'a' file
                }
                else if (right >= 'a' && right <= 'h')
                {
                    boardState.Is960 = true;

                    // Shredder FEN format (Black's rook is on the given file)
                    var rookFile =(byte)(56 + (byte)(right - 'a')); // Map 'a' to file 0, 'b' to 1, ..., 'h' to 7
                    if (rookFile > boardState.BlackKingSquare) // Files e-h (4-7) are kingside castling
                    {
                        boardState.CastleRights |= CastleRights.BlackKingSide;
                        boardState.BlackKingSideTargetSquare = rookFile;
                    }
                    else // Files a-d (0-3) are queenside castling
                    {
                        boardState.CastleRights |= CastleRights.BlackQueenSide;
                        boardState.BlackQueenSideTargetSquare = rookFile;
                    }
                }
            }
        }
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

        SetCastleRights(ref boardState, castleRights);

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

        SetCastleRights(ref boardState, castleRights);

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


    const ulong BlackKingSideCastleBitboardMaskA = 1UL << 60 ^ 1UL << 62;
    const ulong BlackKingSideCastleBitboardMaskB = 1UL << 63 ^ 1UL << 61;
    const ulong BlackQueenSideCastleBitboardMaskA = 1UL << 60 ^ 1UL << 58;
    const ulong BlackQueenSideCastleBitboardMaskB = 1UL << 56 ^ 1UL << 59;
    const ulong WhiteKingSideCastleBitboardMaskA = 1UL << 4 ^ 1UL << 6;
    const ulong WhiteKingSideCastleBitboardMaskB = 1UL << 7 ^ 1UL << 5;
    const ulong WhiteQueenSideCastleBitboardMaskA = 1UL << 4 ^ 1UL << 2;
    const ulong WhiteQueenSideCastleBitboardMaskB = 1UL << 0 ^ 1UL << 3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool PartialApplyWhite(this ref BoardStateData board, uint m)
    {
        var moveType = m.GetMoveType();
        fixed (ulong* occupancy = board.Occupancy)
        {
            if (moveType == 0)
            {
                var movedPiece = m.GetMovedPiece();
                var fromSquare = m.GetFromSquare();
                var toSquare = m.GetToSquare();
                var capturedPiece = m.GetCapturedPiece();
                // Normal move
                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;

                    var pos = ~(1UL << toSquare);
                    *(occupancy + capturedPiece) &= pos;
                    *(occupancy + Constants.BlackPieces) &= pos;
                    if (toSquare == (board.Is960 ? board.BlackQueenSideTargetSquare : 56))
                    {
                        board.CastleRights &= ~CastleRights.BlackQueenSide;
                    }
                    else if (toSquare == (board.Is960 ? board.BlackKingSideTargetSquare : 63))
                    {
                        board.CastleRights &= ~CastleRights.BlackKingSide;
                    }
                }

                var moveMask = (1UL << fromSquare) ^ (1UL << toSquare);
                *(occupancy + movedPiece) ^= moveMask;
                *(occupancy + Constants.WhitePieces) ^= moveMask;

                board.EnPassantFile = 8;

                if (movedPiece == Constants.WhiteKing)
                {
                    board.WhiteKingSquare = toSquare;
                    board.CastleRights &= ~Constants.WhiteCastleRights;
                }
                else if (movedPiece == Constants.WhiteRook)
                {
                    if (fromSquare == (board.Is960 ? board.WhiteQueenSideTargetSquare : 0))
                    {
                        board.CastleRights &= ~CastleRights.WhiteQueenSide;
                    }
                    else if (fromSquare == (board.Is960 ? board.WhiteKingSideTargetSquare : 7))
                    {
                        board.CastleRights &= ~CastleRights.WhiteKingSide;
                    }
                }
            }
            else if (moveType == Constants.DoublePush)
            {
                var fromSquare = m.GetFromSquare();
                var toSquare = m.GetToSquare();

                var moveMask = (1UL << fromSquare) ^ (1UL << toSquare);
                *(occupancy + Constants.WhitePawn) ^= moveMask;
                *(occupancy + Constants.WhitePieces) ^= moveMask;

                board.EnPassantFile = (byte)(fromSquare % 8);
            }
            else if (moveType == Constants.Castle)
            {
                var toSquare = m.GetToSquare();

                if (board.Is960)
                {
                    if (toSquare == board.WhiteKingSideTargetSquare)
                    {
                        // White king side castle
                        const ulong kingTo = 1UL << 6;
                        ulong rookFrom = 1UL << board.WhiteKingSideTargetSquare;
                        const ulong rookTo = 1UL << 5;
                        ulong fromSquare = 1UL << m.GetFromSquare();
                        *(occupancy + Constants.WhiteKing) = (*(occupancy + Constants.WhiteKing) & ~fromSquare) | kingTo;
                        *(occupancy + Constants.WhiteRook) = (*(occupancy + Constants.WhiteRook) & ~rookFrom) | rookTo;
                        *(occupancy + Constants.WhitePieces) = (*(occupancy + Constants.WhitePieces) & ~(fromSquare | rookFrom)) | (kingTo | rookTo);

                        board.WhiteKingSquare = 6;

                    }
                    else if (toSquare == board.WhiteQueenSideTargetSquare)
                    {                        
                        // White queen side castle
                        const ulong kingTo = 1UL << 2;
                        ulong rookFrom = 1UL << board.WhiteQueenSideTargetSquare;
                        const ulong rookTo = 1UL << 3;
                        ulong fromSquare = 1UL << m.GetFromSquare();
                        *(occupancy + Constants.WhiteKing) = (*(occupancy + Constants.WhiteKing) & ~fromSquare) | kingTo;
                        *(occupancy + Constants.WhiteRook) = (*(occupancy + Constants.WhiteRook) & ~rookFrom) | rookTo;
                        *(occupancy + Constants.WhitePieces) = (*(occupancy + Constants.WhitePieces) & ~(fromSquare | rookFrom)) | (kingTo | rookTo);

                        board.WhiteKingSquare = 2;
                    }
                }
                else
                {
                    if (toSquare == 6)
                    {
                        // White king side castle
                        *(occupancy + Constants.WhiteKing) ^= WhiteKingSideCastleBitboardMaskA;
                        *(occupancy + Constants.WhiteRook) ^= WhiteKingSideCastleBitboardMaskB;
                        *(occupancy + Constants.WhitePieces) ^= WhiteKingSideCastleBitboardMaskA | WhiteKingSideCastleBitboardMaskB;
                        board.WhiteKingSquare = 6;
                    }
                    else if (toSquare == 2)
                    {
                        // White queen side castle
                        *(occupancy + Constants.WhiteKing) ^= WhiteQueenSideCastleBitboardMaskA;
                        *(occupancy + Constants.WhiteRook) ^= WhiteQueenSideCastleBitboardMaskB;
                        *(occupancy + Constants.WhitePieces) ^= WhiteQueenSideCastleBitboardMaskA | WhiteQueenSideCastleBitboardMaskB;
                        board.WhiteKingSquare = 2;
                    }
                }

                board.CastleRights &= ~Constants.WhiteCastleRights;
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
                var fromSquare = m.GetFromSquare();
                var toSquare = m.GetToSquare();
                var capturedPiece = m.GetCapturedPiece();

                var promotionPiece = (byte)(Constants.WhitePawn + moveType + moveType - 6);

                var toPos = 1UL << toSquare;
                var fromPos = ~(1UL << fromSquare);

                *(occupancy + promotionPiece) |= toPos;
                *(occupancy + Constants.WhitePawn) &= fromPos;
                *(occupancy + Constants.WhitePieces) = (*(occupancy + Constants.WhitePieces) & fromPos) | toPos;

                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;
                    var pos = ~(1UL << toSquare);
                    *(occupancy + capturedPiece) &= pos;
                    *(occupancy + Constants.BlackPieces) &= pos;
                }

                board.EnPassantFile = 8;
            }
            else
            {
                var fromSquare = m.GetFromSquare();
                var toSquare = m.GetToSquare();
                // Enpassant

                var enpassantSquare = ~(1UL << (byte)(fromSquare.GetRankIndex() * 8 + board.EnPassantFile));
                *(occupancy + Constants.BlackPawn) &= enpassantSquare;
                *(occupancy + Constants.BlackPieces) &= enpassantSquare;

                var moveMask = (1UL << fromSquare) ^ (1UL << toSquare);
                *(occupancy + Constants.WhitePawn) ^= moveMask;
                *(occupancy + Constants.WhitePieces) ^= moveMask;

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
            board.WhiteToMove = false;
            *(occupancy + Constants.Occupancy) = *(occupancy + Constants.WhitePieces) | *(occupancy + Constants.BlackPieces);
        }

        return !board.IsAttackedByBlack(board.WhiteKingSquare);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool PartialApplyBlack(this ref BoardStateData board, uint m)
    {
        var moveType = m.GetMoveType();
        fixed (ulong* occupancy = board.Occupancy)
        {
            if (moveType == 0)
            {
                var movedPiece = m.GetMovedPiece();
                var fromSquare = m.GetFromSquare();
                var toSquare = m.GetToSquare();
                var capturedPiece = m.GetCapturedPiece();

                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;

                    var pos = ~(1UL << toSquare);
                    *(occupancy + capturedPiece) &= pos;
                    *(occupancy + Constants.WhitePieces) &= pos;

                    if (toSquare == (board.Is960 ? board.WhiteQueenSideTargetSquare : 0))
                    {
                        board.CastleRights &= ~CastleRights.WhiteQueenSide;
                    }
                    else if (toSquare == (board.Is960 ? board.WhiteKingSideTargetSquare : 7))
                    {
                        board.CastleRights &= ~CastleRights.WhiteKingSide;
                    }
                }

                // Normal move
                var moveMask = (1UL << fromSquare) ^ (1UL << toSquare);
                *(occupancy + movedPiece) ^= moveMask;
                *(occupancy + Constants.BlackPieces) ^= moveMask;

                board.EnPassantFile = 8;
     
                
                if (movedPiece == Constants.BlackKing)
                {
                    board.BlackKingSquare = toSquare;
                    board.CastleRights &= ~Constants.BlackCastleRights;
                }
                else if (movedPiece == Constants.BlackRook)
                {
                    if (fromSquare == (board.Is960 ? board.BlackQueenSideTargetSquare : 56))
                    {
                        board.CastleRights &= ~CastleRights.BlackQueenSide;
                    }
                    else if (fromSquare == (board.Is960 ? board.BlackKingSideTargetSquare : 63))
                    {
                        board.CastleRights &= ~CastleRights.BlackKingSide;
                    }
                }
            }
            else if (moveType == Constants.DoublePush)
            {
                var fromSquare = m.GetFromSquare();
                var toSquare = m.GetToSquare();

                var moveMask = (1UL << fromSquare) ^ (1UL << toSquare);
                *(occupancy + Constants.BlackPawn) ^= moveMask;
                *(occupancy + Constants.BlackPieces) ^= moveMask;
                
                board.EnPassantFile = (byte)(fromSquare % 8);
            }
            else if (moveType == Constants.Castle)
            {
                var toSquare = m.GetToSquare();

                if (board.Is960)
                {
                    // Castle move
                    if (toSquare == board.BlackKingSideTargetSquare)
                    {
                        // Black king side castle
                        var fromSquare = 1UL << m.GetFromSquare();
                        const ulong kingTo = 1UL << 62;
                        ulong rookFrom = 1UL << board.BlackKingSideTargetSquare;
                        const ulong rookTo = 1UL << 61;

                        *(occupancy + Constants.BlackKing) = (*(occupancy + Constants.BlackKing) & ~fromSquare) | kingTo;
                        *(occupancy + Constants.BlackRook) = (*(occupancy + Constants.BlackRook) & ~rookFrom) | rookTo;
                        *(occupancy + Constants.BlackPieces) = (*(occupancy + Constants.BlackPieces) & ~(fromSquare | rookFrom)) | (kingTo | rookTo);


                        board.BlackKingSquare = 62;
                    }
                    else if (toSquare == board.BlackQueenSideTargetSquare)
                    {
                        // Black queen side castle
                        var fromSquare = 1UL << m.GetFromSquare();
                        const ulong kingTo = 1UL << 58;
                        ulong rookFrom = 1UL << board.BlackQueenSideTargetSquare;
                        const ulong rookTo = 1UL << 59;
                        *(occupancy + Constants.BlackKing) = (*(occupancy + Constants.BlackKing) & ~fromSquare) | kingTo;
                        *(occupancy + Constants.BlackRook) = (*(occupancy + Constants.BlackRook) & ~rookFrom) | rookTo;
                        *(occupancy + Constants.BlackPieces) = (*(occupancy + Constants.BlackPieces) & ~(fromSquare | rookFrom)) | (kingTo | rookTo);

                        board.BlackKingSquare = 58;
                    }
                }
                else
                {
                    // Castle move
                    if (toSquare == 62)
                    {
                        // Black king side castle
                        *(occupancy + Constants.BlackKing) ^= BlackKingSideCastleBitboardMaskA;
                        *(occupancy + Constants.BlackRook) ^= BlackKingSideCastleBitboardMaskB;
                        *(occupancy + Constants.BlackPieces) ^= BlackKingSideCastleBitboardMaskA | BlackKingSideCastleBitboardMaskB;
                        board.BlackKingSquare = 62;
                    }
                    else if (toSquare == 58)
                    {
                        // Black queen side castle
                        *(occupancy + Constants.BlackKing) ^= BlackQueenSideCastleBitboardMaskA;
                        *(occupancy + Constants.BlackRook) ^= BlackQueenSideCastleBitboardMaskB;
                        *(occupancy + Constants.BlackPieces) ^= BlackQueenSideCastleBitboardMaskA | BlackQueenSideCastleBitboardMaskB;
                        board.BlackKingSquare = 58;
                    }
                }

                board.CastleRights &= ~Constants.BlackCastleRights;
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
                var fromSquare = m.GetFromSquare();
                var toSquare = m.GetToSquare();
                var capturedPiece = m.GetCapturedPiece();
                var promotionPiece = (byte)(Constants.BlackPawn + moveType + moveType - 6);
                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;
                    var pos = ~(1UL << toSquare);
                    *(occupancy + capturedPiece) &= pos;
                    *(occupancy + Constants.WhitePieces) &= pos;
                }

                var toPos = 1UL << toSquare;
                var fromPos = ~(1UL << fromSquare);

                *(occupancy + promotionPiece) |= toPos;
                *(occupancy + Constants.BlackPawn) &= fromPos;
                *(occupancy + Constants.BlackPieces) = (*(occupancy + Constants.BlackPieces) & fromPos) | toPos;

                board.EnPassantFile = 8;
            }
            else
            {
                var fromSquare = m.GetFromSquare();
                var toSquare = m.GetToSquare();
                // Enpassant
                var enpassantSquare = ~(1UL << (byte)(fromSquare.GetRankIndex() * 8 + board.EnPassantFile));
                *(occupancy + Constants.WhitePawn) &= enpassantSquare;
                *(occupancy + Constants.WhitePieces) &= enpassantSquare;

                var moveMask = (1UL << fromSquare) ^ (1UL << toSquare);
                *(occupancy + Constants.BlackPawn) ^= moveMask;
                *(occupancy + Constants.BlackPieces) ^= moveMask;

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
            board.WhiteToMove = true;
            *(occupancy + Constants.Occupancy) = *(occupancy + Constants.WhitePieces) | *(occupancy + Constants.BlackPieces);
        }

        return !board.IsAttackedByWhite(board.BlackKingSquare);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateCheckStatus(this ref BoardStateData board)
    {
        board.InCheck = board.WhiteToMove ? board.IsAttackedByBlack(board.WhiteKingSquare) : board.IsAttackedByWhite(board.BlackKingSquare);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FinishApplyWhite(this ref BoardStateData board, ref AccumulatorState accumulatorState, uint m,
        int oldEnpassant,
        CastleRights prevCastle)
    {
        var moveType = m.GetMoveType();

        if (moveType == 0)
        {
            accumulatorState.Move = m;
            var movedPiece = (ushort)m.GetMovedPiece();
            var movedPieceZobristIndex = (movedPiece << 6);
            var movedPieceFeatureIndex = (movedPiece << 7);
            var fromSquare = (ushort)m.GetFromSquare();
            var toSquare = (ushort)m.GetToSquare();
            var capturedPiece = (ushort)m.GetCapturedPiece();

            var deltaHash = *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                         *(Zobrist.PiecesArray + movedPieceZobristIndex + toSquare);
            board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));

            if (movedPiece == Constants.WhitePawn)
            {
                board.PawnHash ^= deltaHash;
            }
            else
            {
                board.WhiteMaterialHash ^= deltaHash;
            }

            if (capturedPiece != Constants.None)
            {
                accumulatorState.ApplyCapture(movedPieceFeatureIndex + (fromSquare << 1),
                    movedPieceFeatureIndex + (toSquare << 1),
                    (capturedPiece << 7) + (toSquare << 1));

                deltaHash = *(Zobrist.PiecesArray + (capturedPiece << 6) + toSquare);
                board.Hash ^= deltaHash;

                if (capturedPiece == Constants.BlackPawn)
                {
                    board.PawnHash ^= deltaHash;
                }
                else
                {
                    board.BlackMaterialHash ^= deltaHash;
                }
            }
            else
            {
                accumulatorState.ApplyQuiet(movedPieceFeatureIndex + (fromSquare << 1),
                    movedPieceFeatureIndex + (toSquare << 1));
            }
        }
        else if (moveType == Constants.DoublePush)
        {
            accumulatorState.Move = m;
            var movedPiece = (ushort)m.GetMovedPiece();
            var movedPieceZobristIndex = (movedPiece << 6);
            var movedPieceFeatureIndex = (movedPiece << 7);
            var fromSquare = (ushort)m.GetFromSquare();
            var toSquare = (ushort)m.GetToSquare();

            accumulatorState.ApplyQuiet(movedPieceFeatureIndex + (fromSquare << 1),
                movedPieceFeatureIndex + (toSquare << 1));
            var deltaHash = *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                    *(Zobrist.PiecesArray + movedPieceZobristIndex + toSquare);

            board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                          *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                          *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
            board.PawnHash ^= deltaHash;
        }
        else if (moveType == Constants.Castle)
        {
            accumulatorState.Move = m;
            var toSquare = (ushort)m.GetToSquare();

            if (board.Is960)
            {
                if (toSquare == board.WhiteKingSideTargetSquare)
                {
                    // White king side castle
                    var fromSquare = (ushort)m.GetFromSquare();
                    accumulatorState.ApplyCastle(Constants.WhiteKingFeatureIndexOffset + (fromSquare << 1), Constants.WhiteKingFeatureIndexOffset + (toSquare << 1),
                        Constants.WhiteRookFeatureIndexOffset + (board.WhiteKingSideTargetSquare << 1), Constants.WhiteRookFeatureIndexOffset + (5 << 1));
                    var deltaHash = Zobrist.PiecesArray[Constants.WhiteKing * 64 + fromSquare] ^
                                    Zobrist.PiecesArray[Constants.WhiteKing * 64 + toSquare] ^
                                    Zobrist.PiecesArray[Constants.WhiteRook * 64 + board.WhiteKingSideTargetSquare] ^
                                    Zobrist.PiecesArray[Constants.WhiteRook * 64 + 5];
                    board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                                  *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                                  *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
                    board.WhiteMaterialHash ^= deltaHash;

                }
                else if (toSquare == board.WhiteQueenSideTargetSquare)
                {
                    // White queen side castle
                    var fromSquare = (ushort)m.GetFromSquare();
                    accumulatorState.ApplyCastle(Constants.WhiteKingFeatureIndexOffset + (fromSquare << 1), Constants.WhiteKingFeatureIndexOffset + (toSquare << 1),
                        Constants.WhiteRookFeatureIndexOffset + (board.WhiteQueenSideTargetSquare << 1), Constants.WhiteRookFeatureIndexOffset + (3 << 1));
                    var deltaHash = Zobrist.PiecesArray[Constants.WhiteKing * 64 + fromSquare] ^
                                    Zobrist.PiecesArray[Constants.WhiteKing * 64 + toSquare] ^
                                    Zobrist.PiecesArray[Constants.WhiteRook * 64 + board.WhiteQueenSideTargetSquare] ^
                                    Zobrist.PiecesArray[Constants.WhiteRook * 64 + 3];
                    board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                                  *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                                  *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
                    board.WhiteMaterialHash ^= deltaHash;
                }
            }
            else
            {
                // Castle move
                if (toSquare == 6)
                {
                    accumulatorState.ApplyCastle(Constants.WhiteKingSideCastleKingFromIndex,
                        Constants.WhiteKingSideCastleKingToIndex,
                        Constants.WhiteKingSideCastleRookFromIndex,
                        Constants.WhiteKingSideCastleRookToIndex);
                    board.Hash ^= Zobrist.WhiteKingSideCastleZobrist ^ Zobrist.SideToMove ^
                                  *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                                  *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
                    board.WhiteMaterialHash ^= Zobrist.WhiteKingSideCastleZobrist;
                }
                else
                {
                    accumulatorState.ApplyCastle(Constants.WhiteQueenSideCastleKingFromIndex,
                        Constants.WhiteQueenSideCastleKingToIndex,
                        Constants.WhiteQueenSideCastleRookFromIndex,
                        Constants.WhiteQueenSideCastleRookToIndex);
                    board.Hash ^= Zobrist.WhiteQueenSideCastleZobrist ^ Zobrist.SideToMove ^
                                  *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                                  *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
                    board.WhiteMaterialHash ^= Zobrist.WhiteQueenSideCastleZobrist;
                }
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
            accumulatorState.Move = m;
            var movedPiece = (ushort)m.GetMovedPiece();
            var movedPieceZobristIndex = (movedPiece << 6);
            var movedPieceFeatureIndex = (movedPiece << 7);
            var fromSquare = (ushort)m.GetFromSquare();
            var toSquare = (ushort)m.GetToSquare();
            var capturedPiece = (ushort)m.GetCapturedPiece();
            var promotionPiece = (ushort)(movedPiece + moveType + moveType - 6);

            var deltaHash = *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare);
            var promotionHash = *(Zobrist.PiecesArray + (promotionPiece << 6) + toSquare);

            board.Hash ^= deltaHash ^
                          promotionHash ^
                          Zobrist.SideToMove ^
                          *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                          *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
            board.WhiteMaterialHash ^= promotionHash;
            board.PawnHash ^= deltaHash;

            if (capturedPiece != Constants.None)
            {
                accumulatorState.ApplyCapture(movedPieceFeatureIndex + (fromSquare << 1),
                    (promotionPiece << 7) + (toSquare << 1),
                    (capturedPiece << 7) + (toSquare << 1));
                deltaHash = *(Zobrist.PiecesArray + (capturedPiece << 6) + toSquare);
                board.Hash ^= deltaHash;
                board.BlackMaterialHash ^= deltaHash;
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
            accumulatorState.Move = m;
            var movedPiece = (ushort)m.GetMovedPiece();
            var movedPieceZobristIndex = (movedPiece << 6);
            var movedPieceFeatureIndex = (movedPiece << 7);
            var fromSquare = (ushort)m.GetFromSquare();
            var toSquare = (ushort)m.GetToSquare();
            var capturedPiece = (ushort)m.GetCapturedPiece();

            var enpassantSquare = (ushort)(fromSquare.GetRankIndex() * 8 + oldEnpassant);
            accumulatorState.ApplyCapture(movedPieceFeatureIndex + (fromSquare << 1),
                movedPieceFeatureIndex + (toSquare << 1),
                (capturedPiece << 7) + (enpassantSquare << 1));

            var deltaHash = *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                        *(Zobrist.PiecesArray + movedPieceZobristIndex + toSquare) ^
                        *(Zobrist.PiecesArray + (capturedPiece << 6) + enpassantSquare);

            board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                          *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                          *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
            board.PawnHash ^= deltaHash;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FinishApplyBlack(this ref BoardStateData board, ref AccumulatorState accumulatorState, uint m,
    int oldEnpassant,
    CastleRights prevCastle)
    {
        var moveType = m.GetMoveType();
        if (moveType == 0)
        {
            accumulatorState.Move = m;
            var movedPiece = (ushort)m.GetMovedPiece();
            var movedPieceZobristIndex = (movedPiece << 6);
            var movedPieceFeatureIndex = (movedPiece << 7);
            var fromSquare = (ushort)m.GetFromSquare();
            var toSquare = (ushort)m.GetToSquare();
            var capturedPiece = (ushort)m.GetCapturedPiece();

            var deltaHash = *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                         *(Zobrist.PiecesArray + movedPieceZobristIndex + toSquare);
            board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                          *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                          *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));

            if (movedPiece == Constants.BlackPawn)
            {
                board.PawnHash ^= deltaHash;
            }
            else
            {
                board.BlackMaterialHash ^= deltaHash;
            }

            if (capturedPiece != Constants.None)
            {
                accumulatorState.ApplyCapture(movedPieceFeatureIndex + (fromSquare << 1),
                    movedPieceFeatureIndex + (toSquare << 1),
                    (capturedPiece << 7) + (toSquare << 1));

                deltaHash = *(Zobrist.PiecesArray + (capturedPiece << 6) + toSquare);
                board.Hash ^= deltaHash;

                if (capturedPiece == Constants.WhitePawn)
                {
                    board.PawnHash ^= deltaHash;
                }
                else
                {
                    board.WhiteMaterialHash ^= deltaHash;
                }
            }
            else
            {
                accumulatorState.ApplyQuiet(movedPieceFeatureIndex + (fromSquare << 1),
                    movedPieceFeatureIndex + (toSquare << 1));
            }
        }
        else if (moveType == Constants.DoublePush)
        {
            accumulatorState.Move = m;
            var movedPiece = (ushort)m.GetMovedPiece();
            var movedPieceZobristIndex = (movedPiece << 6);
            var movedPieceFeatureIndex = (movedPiece << 7);
            var fromSquare = (ushort)m.GetFromSquare();
            var toSquare = (ushort)m.GetToSquare();

            accumulatorState.ApplyQuiet(movedPieceFeatureIndex + (fromSquare << 1),
                movedPieceFeatureIndex + (toSquare << 1));
            var deltaHash = *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                    *(Zobrist.PiecesArray + movedPieceZobristIndex + toSquare);

            board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                          *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                          *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
            board.PawnHash ^= deltaHash;
        }
        else if (moveType == Constants.Castle)
        {
            accumulatorState.Move = m;
            var toSquare = (ushort)m.GetToSquare();

            if (board.Is960)
            {
                // Castle move
                if (toSquare == board.BlackKingSideTargetSquare)
                {
                    // Black king side castle
                    var fromSquare = m.GetFromSquare();
                    accumulatorState.ApplyCastle(Constants.BlackKingFeatureIndexOffset + (fromSquare << 1), Constants.BlackKingFeatureIndexOffset + (toSquare << 1),
                        Constants.BlackRookFeatureIndexOffset + (board.BlackKingSideTargetSquare << 1), Constants.BlackRookFeatureIndexOffset + (61 << 1));
                    var deltaHash = Zobrist.PiecesArray[Constants.BlackKing * 64 + fromSquare] ^
                                               Zobrist.PiecesArray[Constants.BlackKing * 64 + toSquare] ^
                                               Zobrist.PiecesArray[Constants.BlackRook * 64 + board.BlackKingSideTargetSquare] ^
                                               Zobrist.PiecesArray[Constants.BlackRook * 64 + 61];

                    board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                                  *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                                  *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
                    board.BlackMaterialHash ^= deltaHash;
                }
                else if (toSquare == board.BlackQueenSideTargetSquare)
                {
                    // Black queen side castle
                    var fromSquare = m.GetFromSquare();
                    accumulatorState.ApplyCastle(Constants.BlackKingFeatureIndexOffset + (fromSquare << 1), Constants.BlackKingFeatureIndexOffset + (toSquare << 1),
                        Constants.BlackRookFeatureIndexOffset + (board.BlackQueenSideTargetSquare << 1), Constants.BlackRookFeatureIndexOffset + (59 << 1));
                    var deltaHash = Zobrist.PiecesArray[Constants.BlackKing * 64 + fromSquare] ^
                                    Zobrist.PiecesArray[Constants.BlackKing * 64 + toSquare] ^
                                    Zobrist.PiecesArray[Constants.BlackRook * 64 + board.BlackQueenSideTargetSquare] ^
                                    Zobrist.PiecesArray[Constants.BlackRook * 64 + 59];

                    board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                                  *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                                  *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
                    board.BlackMaterialHash ^= deltaHash;
                }
            }
            else
            {
                // Castle move
                if (toSquare == 62)
                {
                    accumulatorState.ApplyCastle(Constants.BlackKingSideCastleKingFromIndex,
                        Constants.BlackKingSideCastleKingToIndex,
                        Constants.BlackKingSideCastleRookFromIndex,
                        Constants.BlackKingSideCastleRookToIndex);
                    board.Hash ^= Zobrist.BlackKingSideCastleZobrist ^ Zobrist.SideToMove ^
                                  *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                                  *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
                    board.BlackMaterialHash ^= Zobrist.BlackKingSideCastleZobrist;
                }
                else
                {
                    accumulatorState.ApplyCastle(Constants.BlackQueenSideCastleKingFromIndex,
                        Constants.BlackQueenSideCastleKingToIndex,
                        Constants.BlackQueenSideCastleRookFromIndex,
                        Constants.BlackQueenSideCastleRookToIndex);
                    board.Hash ^= Zobrist.BlackQueenSideCastleZobrist ^ Zobrist.SideToMove ^
                                  *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                                  *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
                    board.BlackMaterialHash ^= Zobrist.BlackQueenSideCastleZobrist;
                }
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
            accumulatorState.Move = m;
            var movedPiece = (ushort)m.GetMovedPiece();
            var movedPieceZobristIndex = (movedPiece << 6);
            var movedPieceFeatureIndex = (movedPiece << 7);
            var fromSquare = (ushort)m.GetFromSquare();
            var toSquare = (ushort)m.GetToSquare();
            var capturedPiece = (ushort)m.GetCapturedPiece();

            var promotionPiece = (ushort)(movedPiece + moveType + moveType - 6);

            var deltaHash = *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare);
            var promotionDelta = *(Zobrist.PiecesArray + (promotionPiece << 6) + toSquare);
            board.Hash ^= deltaHash ^
                          promotionDelta ^
                          Zobrist.SideToMove ^
                          *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                          *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));

            board.PawnHash ^= deltaHash;
            board.BlackMaterialHash ^= promotionDelta;

            if (capturedPiece != Constants.None)
            {
                accumulatorState.ApplyCapture(movedPieceFeatureIndex + (fromSquare << 1),
                    (promotionPiece << 7) + (toSquare << 1),
                    (capturedPiece << 7) + (toSquare << 1));
                deltaHash = *(Zobrist.PiecesArray + (capturedPiece << 6) + toSquare);
                board.Hash ^= deltaHash;
                board.WhiteMaterialHash ^= deltaHash;
            }
            else
            {
                accumulatorState.ApplyQuiet(movedPieceFeatureIndex + (fromSquare << 1),
                    (promotionPiece << 7) + (toSquare << 1));
            }
        }
        else
        {
            accumulatorState.Move = m;

            // Enpassant
            var movedPiece = (ushort)m.GetMovedPiece();
            var movedPieceZobristIndex = (movedPiece << 6);
            var movedPieceFeatureIndex = (movedPiece << 7);
            var fromSquare = (ushort)m.GetFromSquare();
            var toSquare = (ushort)m.GetToSquare();
            var capturedPiece = (ushort)m.GetCapturedPiece();

            var enpassantSquare = (ushort)(fromSquare.GetRankIndex() * 8 + oldEnpassant);
            accumulatorState.ApplyCapture(movedPieceFeatureIndex + (fromSquare << 1),
                movedPieceFeatureIndex + (toSquare << 1),
                (capturedPiece << 7) + (enpassantSquare << 1));

            var deltaHash = *(Zobrist.PiecesArray + movedPieceZobristIndex + fromSquare) ^
                        *(Zobrist.PiecesArray + movedPieceZobristIndex + toSquare) ^
                        *(Zobrist.PiecesArray + (capturedPiece << 6) + enpassantSquare);

            board.Hash ^= deltaHash ^ Zobrist.SideToMove ^
                          *(Zobrist.DeltaEnpassant + oldEnpassant * 9 + board.EnPassantFile) ^
                          *(Zobrist.DeltaCastleRights + (int)(prevCastle ^ board.CastleRights));
            board.PawnHash ^= deltaHash;
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
        fixed (ulong* occupancy = board.Occupancy)
        {
            if (piece % 2 == 0)
            {
                var pos = 1UL << index;
                *(occupancy + piece) |= pos;
                *(occupancy + Constants.WhitePieces) |= pos;
                if (piece == Constants.WhiteKing)
                {
                    board.WhiteKingSquare = index;
                }
                
                if (piece == Constants.WhitePawn)
                {
                    board.PawnHash ^= Zobrist.PiecesArray[piece * 64 + index];
                }
                else
                {
                    board.WhiteMaterialHash ^= Zobrist.PiecesArray[piece * 64 + index];
                }
            }
            else
            {
                var pos = 1UL << index;
                *(occupancy + piece) |= pos;
                *(occupancy + Constants.BlackPieces) |= pos;

                if (piece == Constants.BlackKing)
                {
                    board.BlackKingSquare = index;
                } 
                
                if (piece == Constants.BlackPawn)
                {
                    board.PawnHash ^= Zobrist.PiecesArray[piece * 64 + index];
                }
                else
                {
                    board.BlackMaterialHash ^= Zobrist.PiecesArray[piece * 64 + index];
                }
            }
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