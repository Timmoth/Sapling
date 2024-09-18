using System.Drawing;
using System;
using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Pgn;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace Sapling.Engine;
#if AVX512
using AvxIntrinsics = System.Runtime.Intrinsics.X86.Avx512BW;
using VectorType = System.Runtime.Intrinsics.Vector512;
using VectorInt = System.Runtime.Intrinsics.Vector512<int>;
using VectorShort = System.Runtime.Intrinsics.Vector512<short>;
#else
using AvxIntrinsics = Avx2;
using VectorType = Vector256;
using VectorInt = Vector256<int>;
using VectorShort = Vector256<short>;
#endif

public static class BoardStateExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanEnPassant(this ref BoardStateData board)
    {
        return board.EnPassantFile < 8;
    }

    public static unsafe BoardState Clone(this BoardState other)
    {
        var board = new BoardState
        {
            Data = other.Data,
        };

        NnueEvaluator.SimdCopy(board.WhiteAccumulator, other.WhiteAccumulator);
        NnueEvaluator.SimdCopy(board.BlackAccumulator, other.BlackAccumulator);
        Unsafe.CopyBlock(board.Moves, other.Moves, sizeof(ulong) * 800);

        return board;
    }

    public static unsafe BoardState ResetTo(this BoardState board, BoardState other)
    {
        other.Data.CloneTo(ref board.Data);
        NnueEvaluator.SimdCopy(board.WhiteAccumulator, other.WhiteAccumulator);
        NnueEvaluator.SimdCopy(board.BlackAccumulator, other.BlackAccumulator);
        Unsafe.CopyBlock(board.Moves, other.Moves, sizeof(ulong) * 800);

        return board;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsufficientMatingMaterial(this ref BoardStateData board)
    {
        if (board.PieceCount > 4)
        {
            return false;
        }

        var black = board.BlackPieces & ~board.BlackKings;
        var white = board.WhitePieces & ~board.WhiteKings;

        var blackPopCount = BitboardHelpers.PopCount(black);
        var whitePopCount = BitboardHelpers.PopCount(white);

        if ((white == 0 && black == board.BlackKnights && blackPopCount <= 2) ||
            (black == 0 && white == board.WhiteKnights && whitePopCount <= 2))
        {
            // Can't mate with only knights
            return true;
        }

        // Can't mate with 1 knight and bishop
        return (black == 0 || (blackPopCount == 1 && (board.BlackKnights | board.BlackBishops) == black)) &&
               (white == 0 || (whitePopCount == 1 && (board.WhiteKnights | board.WhiteBishops) == white));
    }

    public static unsafe BoardState CreateBoardFromArray(Piece[] pieces)
    {
        var boardState = new BoardState();

        for (var i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i];

            if (piece == Constants.None)
            {
                continue;
            }

            boardState.Data.Set((byte)piece, i);
        }

        boardState.Data.CastleRights = Constants.AllCastleRights;
        boardState.Data.WhiteToMove = true;
        boardState.Data.EnPassantFile = 8;
        boardState.Data.Occupancy = boardState.Data.WhitePieces | boardState.Data.BlackPieces;
        boardState.Data.TurnCount = 1;
        boardState.Data.HalfMoveClock = 0;

        boardState.Data.Hash = Zobrist.CalculateZobristKey(ref boardState.Data);
        boardState.Moves[boardState.Data.TurnCount] = boardState.Data.Hash;
        boardState.Data.FillAccumulators(boardState.WhiteAccumulator, boardState.BlackAccumulator);

        boardState.Data.UpdateCheckStatus();
        return boardState;
    }

    public static unsafe BoardState CreateBoardFromFen(string fen)
    {
        var boardState = new BoardState();

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

                    boardState.Data.Set((byte)piece, index);
                    index++;
                }
            }
        }

        boardState.Data.CastleRights = CastleRights.None;
        if (castleRights.Contains("K"))
        {
            boardState.Data.CastleRights |= CastleRights.WhiteKingSide;
        }

        if (castleRights.Contains("Q"))
        {
            boardState.Data.CastleRights |= CastleRights.WhiteQueenSide;
        }

        if (castleRights.Contains("k"))
        {
            boardState.Data.CastleRights |= CastleRights.BlackKingSide;
        }

        if (castleRights.Contains("q"))
        {
            boardState.Data.CastleRights |= CastleRights.BlackQueenSide;
        }

        if (enPassantTarget == "-")
        {
            boardState.Data.EnPassantFile = 8;
        }
        else
        {
            var (file, _) = enPassantTarget.GetPosition();
            boardState.Data.EnPassantFile = (byte)file;
        }

        boardState.Data.TurnCount = fullMoveNumber;
        boardState.Data.HalfMoveClock = halfMoveClock;
        boardState.Data.WhiteToMove = turn == "w";

        boardState.Data.Occupancy = boardState.Data.WhitePieces | boardState.Data.BlackPieces;
        boardState.Data.Hash = Zobrist.CalculateZobristKey(ref boardState.Data);
        boardState.Moves[boardState.Data.TurnCount] = boardState.Data.Hash;
        boardState.Data.FillAccumulators(boardState.WhiteAccumulator, boardState.BlackAccumulator);

        boardState.Data.UpdateCheckStatus();

        return boardState;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Move(this ref BoardStateData board, byte piece, byte fromSquare, byte toSquare)
    {
        var fromMask = 1UL << fromSquare;
        var toMask = 1UL << toSquare;

        switch (piece)
        {
            // White pieces
            case Constants.WhitePawn:
                board.WhitePawns = (board.WhitePawns & ~fromMask) | toMask;
                board.WhitePieces = (board.WhitePieces & ~fromMask) | toMask;
                break;

            case Constants.WhiteRook:
                board.WhiteRooks = (board.WhiteRooks & ~fromMask) | toMask;
                board.WhitePieces = (board.WhitePieces & ~fromMask) | toMask;
                if (fromSquare == 4)
                {
                    board.CastleRights &= ~CastleRights.WhiteQueenSide;
                }
                else if (fromSquare == 7)
                {
                    board.CastleRights &= ~CastleRights.WhiteKingSide;
                }

                break;

            case Constants.WhiteKnight:
                board.WhiteKnights = (board.WhiteKnights & ~fromMask) | toMask;
                board.WhitePieces = (board.WhitePieces & ~fromMask) | toMask;
                break;

            case Constants.WhiteBishop:
                board.WhiteBishops = (board.WhiteBishops & ~fromMask) | toMask;
                board.WhitePieces = (board.WhitePieces & ~fromMask) | toMask;
                break;

            case Constants.WhiteQueen:
                board.WhiteQueens = (board.WhiteQueens & ~fromMask) | toMask;
                board.WhitePieces = (board.WhitePieces & ~fromMask) | toMask;
                break;

            case Constants.WhiteKing:
                board.WhiteKings = (board.WhiteKings & ~fromMask) | toMask;
                board.WhitePieces = (board.WhitePieces & ~fromMask) | toMask;

                board.WhiteKingSquare = toSquare;
                board.CastleRights &= ~Constants.WhiteCastleRights;
                break;

            // Black pieces
            case Constants.BlackPawn:
                board.BlackPawns = (board.BlackPawns & ~fromMask) | toMask;
                board.BlackPieces = (board.BlackPieces & ~fromMask) | toMask;
                break;

            case Constants.BlackRook:
                board.BlackRooks = (board.BlackRooks & ~fromMask) | toMask;
                board.BlackPieces = (board.BlackPieces & ~fromMask) | toMask;

                if (fromSquare == 56)
                {
                    board.CastleRights &= ~CastleRights.BlackQueenSide;
                }
                else if (fromSquare == 63)
                {
                    board.CastleRights &= ~CastleRights.BlackKingSide;
                }

                break;

            case Constants.BlackKnight:
                board.BlackKnights = (board.BlackKnights & ~fromMask) | toMask;
                board.BlackPieces = (board.BlackPieces & ~fromMask) | toMask;
                break;

            case Constants.BlackBishop:
                board.BlackBishops = (board.BlackBishops & ~fromMask) | toMask;
                board.BlackPieces = (board.BlackPieces & ~fromMask) | toMask;
                break;

            case Constants.BlackQueen:
                board.BlackQueens = (board.BlackQueens & ~fromMask) | toMask;
                board.BlackPieces = (board.BlackPieces & ~fromMask) | toMask;
                break;

            case Constants.BlackKing:
                board.BlackKings = (board.BlackKings & ~fromMask) | toMask;
                board.BlackPieces = (board.BlackPieces & ~fromMask) | toMask;

                board.BlackKingSquare = toSquare;
                board.CastleRights &= ~Constants.BlackCastleRights;

                break;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static void Add(this ref BoardStateData board, byte piece, byte square)
    {
        var pos = 1UL << square;

        switch (piece)
        {
            // White pieces
            case Constants.WhitePawn:
                board.WhitePawns |= pos;
                board.WhitePieces |= pos;
                break;

            case Constants.WhiteRook:
                board.WhiteRooks |= pos;
                board.WhitePieces |= pos;
                break;

            case Constants.WhiteKnight:
                board.WhiteKnights |= pos;
                board.WhitePieces |= pos;
                break;

            case Constants.WhiteBishop:
                board.WhiteBishops |= pos;
                board.WhitePieces |= pos;
                break;

            case Constants.WhiteQueen:
                board.WhiteQueens |= pos;
                board.WhitePieces |= pos;
                break;

            case Constants.WhiteKing:
                board.WhiteKings |= pos;
                board.WhitePieces |= pos;
                board.WhiteKingSquare = square;
                break;

            // Black pieces
            case Constants.BlackPawn:
                board.BlackPawns |= pos;
                board.BlackPieces |= pos;
                break;

            case Constants.BlackRook:
                board.BlackRooks |= pos;
                board.BlackPieces |= pos;
                break;

            case Constants.BlackKnight:
                board.BlackKnights |= pos;
                board.BlackPieces |= pos;
                break;

            case Constants.BlackBishop:
                board.BlackBishops |= pos;
                board.BlackPieces |= pos;
                break;

            case Constants.BlackQueen:
                board.BlackQueens |= pos;
                board.BlackPieces |= pos;
                break;

            case Constants.BlackKing:
                board.BlackKings |= pos;
                board.BlackPieces |= pos;
                board.BlackKingSquare = square;
                break;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    public static void Remove(this ref BoardStateData board, byte piece, byte square)
    {
        var pos = ~(1UL << square);
        switch (piece)
        {
            // White pieces
            case Constants.WhitePawn:
                board.WhitePieces &= pos;
                board.WhitePawns &= pos;
                break;

            case Constants.WhiteRook:
                board.WhitePieces &= pos;
                board.WhiteRooks &= pos;
                break;

            case Constants.WhiteKnight:
                board.WhitePieces &= pos;
                board.WhiteKnights &= pos;
                break;

            case Constants.WhiteBishop:
                board.WhitePieces &= pos;
                board.WhiteBishops &= pos;
                break;

            case Constants.WhiteQueen:
                board.WhitePieces &= pos;
                board.WhiteQueens &= pos;
                break;

            case Constants.WhiteKing:
                board.WhitePieces &= pos;
                board.WhiteKings &= pos;
                break;

            // Black pieces
            case Constants.BlackPawn:
                board.BlackPieces &= pos;
                board.BlackPawns &= pos;
                break;

            case Constants.BlackRook:
                board.BlackPieces &= pos;
                board.BlackRooks &= pos;
                break;

            case Constants.BlackKnight:
                board.BlackPieces &= pos;
                board.BlackKnights &= pos;
                break;

            case Constants.BlackBishop:
                board.BlackPieces &= pos;
                board.BlackBishops &= pos;
                break;

            case Constants.BlackQueen:
                board.BlackPieces &= pos;
                board.BlackQueens &= pos;
                break;

            case Constants.BlackKing:
                board.BlackPieces &= pos;
                board.BlackKings &= pos;
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool PartialApply(this ref BoardStateData board, uint m)
    {
        var (movedPiece, fromSquare, toSquare, capturedPiece, moveType) = m.Deconstruct();

        switch (moveType)
        {
            case Constants.EnPassant:
            {
                board.Move(movedPiece, fromSquare, toSquare);

                var enpassantSquare = (byte)(fromSquare.GetRankIndex() * 8 + board.EnPassantFile);
                board.Remove(capturedPiece, enpassantSquare);

                // Clear enpassant file
                board.EnPassantFile = 8;
                --board.PieceCount;
                break;
            }
            case >= 4:
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
                board.Add(promotionPiece, toSquare);
                board.Remove(movedPiece, fromSquare);
                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;
                    board.Remove(capturedPiece, toSquare);
                }

                board.EnPassantFile = 8;
                break;
            }
            case Constants.DoublePush:
                board.Move(movedPiece, fromSquare, toSquare);

                board.EnPassantFile = (byte)(fromSquare % 8);
                break;
            case Constants.Castle when toSquare == 62:
                board.Move(Constants.BlackRook, 63, 61);
                board.Move(Constants.BlackKing, fromSquare, toSquare);
                board.EnPassantFile = 8; // Reset
                break;
            case Constants.Castle when toSquare == 58:
                board.Move(Constants.BlackRook, 56, 59);
                board.Move(Constants.BlackKing, fromSquare, toSquare);
                board.EnPassantFile = 8; // Reset
                break;
            case Constants.Castle when toSquare == 6:
                board.Move(Constants.WhiteRook, 7, 5);
                board.Move(Constants.WhiteKing, fromSquare, toSquare);
                board.EnPassantFile = 8; // Reset
                break;
            case Constants.Castle:
            {
                if (toSquare == 2)
                {
                    board.Move(Constants.WhiteRook, 0, 3);
                    board.Move(Constants.WhiteKing, fromSquare, toSquare);
                    board.EnPassantFile = 8; // Reset
                }

                break;
            }
            default:
            {
                // Normal move
                board.Move(movedPiece, fromSquare, toSquare);

                board.EnPassantFile = 8;
                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;
                    board.Remove(capturedPiece, toSquare);
                }

                break;
            }
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
        board.Occupancy = board.WhitePieces | board.BlackPieces;

        return (!board.WhiteToMove && !board.IsAttackedByBlack(board.WhiteKingSquare)) ||
               (board.WhiteToMove && !board.IsAttackedByWhite(board.BlackKingSquare));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateCheckStatus(this ref BoardStateData board)
    {
        if (board.WhiteToMove)
        {
            board.InCheck = board.IsAttackedByBlack(board.WhiteKingSquare);
        }
        else
        {
            board.InCheck = board.IsAttackedByWhite(board.BlackKingSquare);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void FinishApply(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc, ulong* hashHistory, uint m,
        int oldEnpassant,
        CastleRights prevCastle)
    {
        board.Hash ^= Zobrist.SideToMove;

        var (movedPiece, fromSquare, toSquare, capturedPiece, moveType) = m.Deconstruct();

        switch (moveType)
        {
            case Constants.EnPassant:
            {
                board.Replace(whiteAcc, blackAcc, movedPiece, fromSquare, toSquare);

                var enpassantSquare = fromSquare.GetRankIndex() * 8 + oldEnpassant;
                board.Deactivate(whiteAcc, blackAcc, capturedPiece, enpassantSquare);

                board.Hash ^= Zobrist.PiecesArray[movedPiece * 64 + fromSquare] ^
                                   Zobrist.PiecesArray[movedPiece * 64 + toSquare] ^
                                   Zobrist.PiecesArray[capturedPiece * 64 + enpassantSquare];
                break;
            }
            case >= 4:
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

                var promotionPiece = movedPiece + moveType + moveType - 6;
                board.Apply(whiteAcc, blackAcc, promotionPiece, toSquare);
                board.Deactivate(whiteAcc, blackAcc, movedPiece, fromSquare);
                board.Hash ^= Zobrist.PiecesArray[movedPiece * 64 + fromSquare] ^
                                   Zobrist.PiecesArray[promotionPiece * 64 + toSquare];

                if (capturedPiece != Constants.None)
                {
                    board.Deactivate(whiteAcc, blackAcc, capturedPiece, toSquare);
                    board.Hash ^= Zobrist.PiecesArray[capturedPiece * 64 + toSquare];
                }

                break;
            }
            case Constants.DoublePush:
                board.Replace(whiteAcc, blackAcc, movedPiece, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[movedPiece * 64 + fromSquare] ^
                                   Zobrist.PiecesArray[movedPiece * 64 + toSquare];
                break;
            case Constants.Castle when toSquare == 62:
                board.Replace(whiteAcc, blackAcc, Constants.BlackRook, 63, 61);
                board.Replace(whiteAcc, blackAcc, Constants.BlackKing, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[Constants.BlackKing * 64 + fromSquare] ^
                                   Zobrist.PiecesArray[Constants.BlackKing * 64 + toSquare] ^
                                   Zobrist.PiecesArray[Constants.BlackRook * 64 + 63] ^
                                   Zobrist.PiecesArray[Constants.BlackRook * 64 + 61];
                break;
            case Constants.Castle when toSquare == 58:
                board.Replace(whiteAcc, blackAcc, Constants.BlackRook, 56, 59);
                board.Replace(whiteAcc, blackAcc, Constants.BlackKing, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[Constants.BlackKing * 64 + fromSquare] ^
                                   Zobrist.PiecesArray[Constants.BlackKing * 64 + toSquare]
                                   ^ Zobrist.PiecesArray[Constants.BlackRook * 64 + 56] ^
                                   Zobrist.PiecesArray[Constants.BlackRook * 64 + 59];
                break;
            case Constants.Castle when toSquare == 6:
                board.Replace(whiteAcc, blackAcc, Constants.WhiteRook, 7, 5);
                board.Replace(whiteAcc, blackAcc, Constants.WhiteKing, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[Constants.WhiteKing * 64 + fromSquare] ^
                                   Zobrist.PiecesArray[Constants.WhiteKing * 64 + toSquare] ^
                                   Zobrist.PiecesArray[Constants.WhiteRook * 64 + 7] ^
                                   Zobrist.PiecesArray[Constants.WhiteRook * 64 + 5];
                break;
            case Constants.Castle:
            {
                if (toSquare == 2)
                {
                    board.Replace(whiteAcc, blackAcc, Constants.WhiteRook, 0, 3);
                    board.Replace(whiteAcc, blackAcc, Constants.WhiteKing, fromSquare, toSquare);
                    board.Hash ^= Zobrist.PiecesArray[Constants.WhiteKing * 64 + fromSquare] ^
                                       Zobrist.PiecesArray[Constants.WhiteKing * 64 + toSquare] ^
                                       Zobrist.PiecesArray[Constants.WhiteRook * 64 + 0] ^
                                       Zobrist.PiecesArray[Constants.WhiteRook * 64 + 3];
                }

                break;
            }
            default:
            {
                board.Replace(whiteAcc, blackAcc, movedPiece, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[movedPiece * 64 + fromSquare] ^
                                   Zobrist.PiecesArray[movedPiece * 64 + toSquare];

                if (capturedPiece != Constants.None)
                {
                    board.Deactivate(whiteAcc, blackAcc, capturedPiece, toSquare);
                    board.Hash ^= Zobrist.PiecesArray[capturedPiece * 64 + toSquare];
                }

                break;
            }
        }

        if (oldEnpassant != board.EnPassantFile)
        {
            if (oldEnpassant < 8)
            {
                board.Hash ^= Zobrist.EnPassantFile[oldEnpassant];
            }

            if (board.EnPassantFile < 8)
            {
                board.Hash ^= Zobrist.EnPassantFile[board.EnPassantFile];
            }
        }

        if ((prevCastle & CastleRights.WhiteKingSide) != (board.CastleRights & CastleRights.WhiteKingSide))
        {
            board.Hash ^= Zobrist.WhiteKingSideCastlingRights;
        }

        if ((prevCastle & CastleRights.WhiteQueenSide) != (board.CastleRights & CastleRights.WhiteQueenSide))
        {
            board.Hash ^= Zobrist.WhiteQueenSideCastlingRights;
        }

        if ((prevCastle & CastleRights.BlackKingSide) != (board.CastleRights & CastleRights.BlackKingSide))
        {
            board.Hash ^= Zobrist.BlackKingSideCastlingRights;
        }

        if ((prevCastle & CastleRights.BlackQueenSide) != (board.CastleRights & CastleRights.BlackQueenSide))
        {
            board.Hash ^= Zobrist.BlackQueenSideCastlingRights;
        }

        if (movedPiece == Constants.WhiteKing && fromSquare.IsMirroredSide() != toSquare.IsMirroredSide())
        {
            board.ShouldWhiteMirrored = toSquare.IsMirroredSide();
        }
        else if (movedPiece == Constants.BlackKing && fromSquare.IsMirroredSide() != toSquare.IsMirroredSide())
        {
            board.ShouldBlackMirrored = toSquare.IsMirroredSide();
        }

        hashHistory[board.TurnCount - 1] = board.Hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ApplyNullMove(this ref BoardStateData board)
    {
        board.Hash ^= Zobrist.SideToMove;

        if (board.EnPassantFile != 8)
        {
            board.Hash ^= Zobrist.EnPassantFile[board.EnPassantFile];
            board.EnPassantFile = 8;
        }

        board.TurnCount++;
        board.WhiteToMove = !board.WhiteToMove;
        board.InCheck = false;
        board.HalfMoveClock = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UnApplyNullMove(this ref BoardStateData board, ulong oldHash, byte oldEnpassant, bool oldCheck, int oldHalfMoveClock)
    {
        board.Hash = oldHash;
        board.EnPassantFile = oldEnpassant;
        board.TurnCount--;
        board.WhiteToMove = !board.WhiteToMove;
        board.InCheck = oldCheck;
        board.HalfMoveClock = oldHalfMoveClock;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Set(this ref BoardStateData board, byte piece, int index)
    {
        board.PieceCount++;
        board.Add(piece, (byte)index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSquareOccupied(this ref BoardStateData board, ulong position)
    {
        return (board.Occupancy & position) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEmptySquare(this ref BoardStateData board, ulong position)
    {
        return (board.Occupancy & position) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasMajorPieces(this ref BoardStateData board, bool isWhite)
    {
        if (isWhite)
        {
            return (board.WhitePieces & ~(board.WhitePawns | board.WhiteKings)) > 0;
        }

        return (board.BlackPieces & ~(board.BlackPawns | board.BlackKings)) > 0;
    }

    public static unsafe void Apply(this ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc, ulong* hashHistory, uint move)
    {
        var prevEnpassant = board.EnPassantFile;
        var prevCastleRights = board.CastleRights;

        board.PartialApply(move);
        board.UpdateCheckStatus();
        board.FinishApply(whiteAcc, blackAcc, hashHistory, move, prevEnpassant, prevCastleRights);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetPiece(this ref BoardStateData board, int square)
    {
        if ((board.Occupancy & (1UL << square)) == 0)
        {
            return 0;
        }

        return (byte) Bmi1.X64.TrailingZeroCount(
                          (board.BlackPawns >> square & 1UL) << 1 |
                          (board.WhitePawns >> square & 1UL) << 2 |
                          (board.BlackKnights >> square & 1UL) << 3 |
                          (board.WhiteKnights >> square & 1UL) << 4 |
                          (board.BlackBishops >> square & 1UL) << 5 |
                          (board.WhiteBishops >> square & 1UL) << 6 |
                          (board.BlackRooks >> square & 1UL) << 7 |
                          (board.WhiteRooks >> square & 1UL) << 8 |
                          (board.BlackQueens >> square & 1UL) << 9 |
                          (board.WhiteQueens >> square & 1UL) << 10 |
                          (board.BlackKings >> square & 1UL) << 11 |
                      (board.WhiteKings >> square & 1UL) << 12);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetWhitePiece(this ref BoardStateData board, byte square)
    {
        return (byte)Bmi1.X64.TrailingZeroCount((board.WhitePawns >> square & 1UL) << 2 |
                                                  (board.WhiteKnights >> square & 1UL) << 4 |
                                                  (board.WhiteBishops >> square & 1UL) << 6 |
                                                  (board.WhiteRooks >> square & 1UL) << 8 |
                                                  (board.WhiteQueens >> square & 1UL) << 10 |
                                                  (board.WhiteKings >> square & 1UL) << 12);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetBlackPiece(this ref BoardStateData board, byte square)
    {
        return (byte)Bmi1.X64.TrailingZeroCount((board.BlackPawns >> square & 1UL) << 1 |
                                                  (board.BlackKnights >> square & 1UL) << 3 |
                                                  (board.BlackBishops >> square & 1UL) << 5 |
                                                  (board.BlackRooks >> square & 1UL) << 7 |
                                                  (board.BlackQueens >> square & 1UL) << 9 |
                                                  (board.BlackKings >> square & 1UL) << 11);
    }
}