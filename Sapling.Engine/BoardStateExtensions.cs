using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Pgn;

namespace Sapling.Engine;

public static class BoardStateExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanEnPassant(this BoardState board)
    {
        return board.EnPassantFile < 8;
    }

    public static BoardState Clone(this BoardState other)
    {
        var board = new BoardState
        {
            Occupancy = other.Occupancy,
            PieceCount = other.PieceCount,
            Hash = other.Hash,
            InCheck = other.InCheck,
            HalfMoveClock = other.HalfMoveClock,
            TurnCount = other.TurnCount,
            RepetitionPositionHistory = new Stack<ulong>(other.RepetitionPositionHistory),
            WhiteToMove = other.WhiteToMove,
            WhitePawns = other.WhitePawns,
            WhiteKnights = other.WhiteKnights,
            WhiteBishops = other.WhiteBishops,
            WhiteRooks = other.WhiteRooks,
            WhiteQueens = other.WhiteQueens,
            WhiteKings = other.WhiteKings,
            WhiteKingSquare = other.WhiteKingSquare,
            WhitePieces = other.WhitePieces,
            BlackPieces = other.BlackPieces,
            BlackPawns = other.BlackPawns,
            BlackKnights = other.BlackKnights,
            BlackBishops = other.BlackBishops,
            BlackRooks = other.BlackRooks,
            BlackQueens = other.BlackQueens,
            BlackKings = other.BlackKings,
            BlackKingSquare = other.BlackKingSquare,
            Evaluator = NnueEvaluator.Clone(other.Evaluator),
            CastleRights = other.CastleRights,
            EnPassantFile = other.EnPassantFile
        };

        Array.Copy(other.Pieces, board.Pieces, 64);

        return board;
    }

    public static BoardState ResetTo(this BoardState board, BoardState other)
    {
        board.Occupancy = other.Occupancy;
        board.PieceCount = other.PieceCount;
        board.Hash = other.Hash;
        board.InCheck = other.InCheck;
        board.HalfMoveClock = other.HalfMoveClock;
        board.TurnCount = other.TurnCount;
        board.RepetitionPositionHistory = new Stack<ulong>(other.RepetitionPositionHistory);
        board.WhiteToMove = other.WhiteToMove;
        board.WhitePawns = other.WhitePawns;
        board.WhiteKnights = other.WhiteKnights;
        board.WhiteBishops = other.WhiteBishops;
        board.WhiteRooks = other.WhiteRooks;
        board.WhiteQueens = other.WhiteQueens;
        board.WhiteKings = other.WhiteKings;
        board.WhiteKingSquare = other.WhiteKingSquare;
        board.WhitePieces = other.WhitePieces;
        board.BlackPieces = other.BlackPieces;
        board.BlackPawns = other.BlackPawns;
        board.BlackKnights = other.BlackKnights;
        board.BlackBishops = other.BlackBishops;
        board.BlackRooks = other.BlackRooks;
        board.BlackQueens = other.BlackQueens;
        board.BlackKings = other.BlackKings;
        board.BlackKingSquare = other.BlackKingSquare;
        board.CastleRights = other.CastleRights;
        board.EnPassantFile = other.EnPassantFile;

        board.Evaluator.ResetTo(other.Evaluator);
        Array.Copy(other.Pieces, board.Pieces, 64);

        return board;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InsufficientMatingMaterial(this BoardState board)
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

    public static BoardState CreateBoardFromArray(Piece[] pieces)
    {
        var boardState = new BoardState();

        for (var i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i];

            if (piece == Constants.None)
            {
                continue;
            }

            boardState.Set((byte)piece, i);
        }

        boardState.WhiteToMove = true;
        boardState.EnPassantFile = 8;
        boardState.Occupancy = boardState.WhitePieces | boardState.BlackPieces;

        boardState.Hash = Zobrist.CalculateZobristKey(boardState);

        boardState.Evaluator = new NnueEvaluator();
        boardState.Evaluator.FillWhiteAccumulator(boardState, boardState.WhiteKingSquare.IsMirroredSide());
        boardState.Evaluator.FillBlackAccumulator(boardState, boardState.BlackKingSquare.IsMirroredSide());
        return boardState;
    }

    public static BoardState CreateBoardFromFen(string fen)
    {
        var boardState = new BoardState();

        var parts = fen.Split(' ');

        var rows = parts[0].Split('/');
        var turn = parts[1];
        var castleRights = parts[2];
        var enPassantRights = parts[3];

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

                    boardState.Set((byte)piece, index);
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

        if (enPassantRights == "-")
        {
            boardState.EnPassantFile = 8;
        }
        else
        {
            var (file, _) = enPassantRights.GetPosition();
            boardState.EnPassantFile = (byte)file;
        }

        boardState.TurnCount = turn == "w" ? (ushort)0 : (ushort)1;
        boardState.WhiteToMove = turn == "w";

        boardState.Occupancy = boardState.WhitePieces | boardState.BlackPieces;
        boardState.Hash = Zobrist.CalculateZobristKey(boardState);
        boardState.Evaluator = new NnueEvaluator();
        boardState.Evaluator.FillWhiteAccumulator(boardState, boardState.WhiteKingSquare.IsMirroredSide());
        boardState.Evaluator.FillBlackAccumulator(boardState, boardState.WhiteKingSquare.IsMirroredSide());

        return boardState;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Move(this BoardState board, byte piece, byte fromSquare, byte toSquare)
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
                    board.CastleRights &= ~Constants.WhiteCastleRights;
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
    public static void Add(this BoardState board, byte piece, byte square)
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
    public static void Remove(this BoardState board, byte piece, byte square)
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
    public static bool PartialApply(this BoardState board, uint m)
    {
        var (movedPiece, fromSquare, toSquare, capturedPiece, moveType) = m.Deconstruct();

        switch (moveType)
        {
            case Constants.EnPassant:
            {
                board.Pieces[toSquare] = movedPiece;
                board.Move(movedPiece, fromSquare, toSquare);

                var enpassantSquare = (byte)(fromSquare.GetRankIndex() * 8 + board.EnPassantFile);
                board.Pieces[fromSquare] = board.Pieces[enpassantSquare] = Constants.None;
                board.Remove(capturedPiece, enpassantSquare);

                // Clear enpassant file
                board.EnPassantFile = 8;
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
                board.Pieces[toSquare] = promotionPiece;
                board.Pieces[fromSquare] = Constants.None;
                board.Add(promotionPiece, toSquare);
                board.Remove(movedPiece, fromSquare);
                if (capturedPiece != Constants.None)
                {
                    board.Remove(capturedPiece, toSquare);
                }

                board.EnPassantFile = 8;
                break;
            }
            case Constants.DoublePush:
                board.Pieces[toSquare] = movedPiece;
                board.Pieces[fromSquare] = Constants.None;
                board.Move(movedPiece, fromSquare, toSquare);

                board.EnPassantFile = (byte)(fromSquare % 8);
                break;
            case Constants.Castle when toSquare == 62:
                board.Pieces[fromSquare] = board.Pieces[63] = Constants.None;
                board.Pieces[toSquare] = Constants.BlackKing;
                board.Pieces[61] = Constants.BlackRook;
                board.Move(Constants.BlackRook, 63, 61);
                board.Move(Constants.BlackKing, fromSquare, toSquare);
                board.EnPassantFile = 8; // Reset
                break;
            case Constants.Castle when toSquare == 58:
                board.Pieces[fromSquare] = board.Pieces[59] = Constants.None;
                board.Pieces[toSquare] = Constants.BlackKing;
                board.Pieces[56] = Constants.BlackRook;
                board.Move(Constants.BlackRook, 56, 59);
                board.Move(Constants.BlackKing, fromSquare, toSquare);
                board.EnPassantFile = 8; // Reset
                break;
            case Constants.Castle when toSquare == 6:
                board.Pieces[fromSquare] = board.Pieces[7] = Constants.None;
                board.Pieces[toSquare] = Constants.WhiteKing;
                board.Pieces[5] = Constants.WhiteRook;
                board.Move(Constants.WhiteRook, 7, 5);
                board.Move(Constants.WhiteKing, fromSquare, toSquare);
                board.EnPassantFile = 8; // Reset
                break;
            case Constants.Castle:
            {
                if (toSquare == 2)
                {
                    board.Pieces[fromSquare] = board.Pieces[3] = Constants.None;
                    board.Pieces[toSquare] = Constants.WhiteKing;
                    board.Pieces[0] = Constants.WhiteRook;
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

                board.Pieces[toSquare] = movedPiece;
                board.Pieces[fromSquare] = Constants.None;

                board.EnPassantFile = 8;
                if (capturedPiece != Constants.None)
                {
                    board.Remove(capturedPiece, toSquare);
                }

                break;
            }
        }

        board.TurnCount++;
        board.WhiteToMove = !board.WhiteToMove;
        board.Occupancy = board.WhitePieces | board.BlackPieces;

        return (!board.WhiteToMove && !board.IsAttackedByBlack(board.WhiteKingSquare)) ||
               (board.WhiteToMove && !board.IsAttackedByWhite(board.BlackKingSquare));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateCheckStatus(this BoardState board)
    {
        board.InCheck = (board.WhiteToMove && board.IsAttackedByBlack(board.WhiteKingSquare)) ||
                        (!board.WhiteToMove && board.IsAttackedByWhite(board.BlackKingSquare));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FinishApply(this BoardState board, uint m,
        int oldEnpassant,
        CastleRights prevCastle)
    {
        board.Hash ^= Zobrist.SideToMove;

        var (movedPiece, fromSquare, toSquare, capturedPiece, moveType) = m.Deconstruct();

        if (movedPiece == Constants.WhiteKing && toSquare.IsMirroredSide() != board.Evaluator.WhiteMirrored)
        {
            board.Evaluator.MirrorWhite(board, toSquare.IsMirroredSide());
        }else if (moveType == Constants.BlackKing && toSquare.IsMirroredSide() != board.Evaluator.BlackMirrored)
        {
            board.Evaluator.MirrorBlack(board, toSquare.IsMirroredSide());
        }

        switch (moveType)
        {
            case Constants.EnPassant:
            {
                board.Evaluator.Replace(movedPiece, fromSquare, toSquare);

                var enpassantSquare = fromSquare.GetRankIndex() * 8 + oldEnpassant;
                board.Evaluator.Deactivate(capturedPiece, enpassantSquare);

                board.Hash ^= Zobrist.PiecesArray[movedPiece * 64 + fromSquare] ^
                              Zobrist.PiecesArray[movedPiece * 64 + toSquare] ^
                              Zobrist.PiecesArray[capturedPiece * 64 + enpassantSquare];
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

                var promotionPiece = movedPiece + moveType + moveType - 6;
                board.Evaluator.Apply(promotionPiece, toSquare);
                board.Evaluator.Deactivate(movedPiece, fromSquare);
                board.Hash ^= Zobrist.PiecesArray[movedPiece * 64 + fromSquare] ^
                              Zobrist.PiecesArray[promotionPiece * 64 + toSquare];

                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;
                    board.Evaluator.Deactivate(capturedPiece, toSquare);
                    board.Hash ^= Zobrist.PiecesArray[capturedPiece * 64 + toSquare];
                }

                break;
            }
            case Constants.DoublePush:
                board.Evaluator.Replace(movedPiece, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[movedPiece * 64 + fromSquare] ^
                              Zobrist.PiecesArray[movedPiece * 64 + toSquare];
                break;
            case Constants.Castle when toSquare == 62:
                board.Evaluator.Replace(Constants.BlackRook, 63, 61);
                board.Evaluator.Replace(Constants.BlackKing, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[Constants.BlackKing * 64 + fromSquare] ^
                              Zobrist.PiecesArray[Constants.BlackKing * 64 + toSquare] ^
                              Zobrist.PiecesArray[Constants.BlackRook * 64 + 63] ^
                              Zobrist.PiecesArray[Constants.BlackRook * 64 + 61];
                break;
            case Constants.Castle when toSquare == 58:
                board.Evaluator.Replace(Constants.BlackRook, 56, 59);
                board.Evaluator.Replace(Constants.BlackKing, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[Constants.BlackKing * 64 + fromSquare] ^
                              Zobrist.PiecesArray[Constants.BlackKing * 64 + toSquare]
                              ^ Zobrist.PiecesArray[Constants.BlackRook * 64 + 56] ^
                              Zobrist.PiecesArray[Constants.BlackRook * 64 + 59];
                break;
            case Constants.Castle when toSquare == 6:
                board.Evaluator.Replace(Constants.WhiteRook, 7, 5);
                board.Evaluator.Replace(Constants.WhiteKing, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[Constants.WhiteKing * 64 + fromSquare] ^
                              Zobrist.PiecesArray[Constants.WhiteKing * 64 + toSquare] ^
                              Zobrist.PiecesArray[Constants.WhiteRook * 64 + 7] ^
                              Zobrist.PiecesArray[Constants.WhiteRook * 64 + 5];
                break;
            case Constants.Castle:
            {
                if (toSquare == 2)
                {
                    board.Evaluator.Replace(Constants.WhiteRook, 0, 3);
                    board.Evaluator.Replace(Constants.WhiteKing, fromSquare, toSquare);
                    board.Hash ^= Zobrist.PiecesArray[Constants.WhiteKing * 64 + fromSquare] ^
                                  Zobrist.PiecesArray[Constants.WhiteKing * 64 + toSquare] ^
                                  Zobrist.PiecesArray[Constants.WhiteRook * 64 + 0] ^
                                  Zobrist.PiecesArray[Constants.WhiteRook * 64 + 3];
                }

                break;
            }
            default:
            {
                board.Evaluator.Replace(movedPiece, fromSquare, toSquare);
                board.Hash ^= Zobrist.PiecesArray[movedPiece * 64 + fromSquare] ^
                              Zobrist.PiecesArray[movedPiece * 64 + toSquare];

                if (capturedPiece != Constants.None)
                {
                    --board.PieceCount;
                    board.Evaluator.Deactivate(capturedPiece, toSquare);
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

        if (m.IsReset())
        {
            board.HalfMoveClock = 0;
        }
        else
        {
            board.HalfMoveClock++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateRepetitions(this BoardState board, uint move)
    {
        if (move.IsReset())
        {
            board.HalfMoveClock = 0;
            board.RepetitionPositionHistory.Clear();
        }
        else
        {
            board.HalfMoveClock++;
        }

        board.RepetitionPositionHistory.Push(board.Hash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PartialUnApply(this BoardState board, uint m,
        ulong oldHash,
        byte oldEnpassant,
        bool prevWhiteCheck,
        CastleRights prevCastleRights,
        int prevFiftyMoveCounter)
    {
        board.InCheck = prevWhiteCheck;
        board.EnPassantFile = oldEnpassant;
        board.TurnCount--;
        board.WhiteToMove = !board.WhiteToMove;
        board.HalfMoveClock = prevFiftyMoveCounter;
        board.Hash = oldHash;

        var (movedPiece, fromSquare, toSquare, capturedPiece, moveType) = m.Deconstruct();

        switch (moveType)
        {
            case Constants.EnPassant:
            {
                board.Pieces[fromSquare] = movedPiece;
                board.Pieces[toSquare] = Constants.None;
                board.Move(movedPiece, toSquare, fromSquare);
                var enpassantIndex = (byte)(fromSquare.GetRankIndex() * 8 + board.EnPassantFile);
                board.Add(capturedPiece, enpassantIndex);

                board.Pieces[enpassantIndex] = capturedPiece;
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

                board.Pieces[fromSquare] = movedPiece;
                board.Pieces[toSquare] = capturedPiece;

                board.Add(movedPiece, fromSquare);
                board.Remove((byte)(movedPiece + moveType + moveType - 6), toSquare);

                if (capturedPiece != Constants.None)
                {
                    board.Add(capturedPiece, toSquare);
                }

                break;
            }
            case Constants.Castle when toSquare == 62:
                board.Pieces[fromSquare] = Constants.BlackKing;
                board.Pieces[63] = Constants.BlackRook;
                board.Pieces[toSquare] = board.Pieces[61] = Constants.None;

                board.Move(Constants.BlackRook, 61, 63);
                board.Move(Constants.BlackKing, toSquare, fromSquare);
                break;
            case Constants.Castle when toSquare == 58:
                board.Pieces[fromSquare] = Constants.BlackKing;
                board.Pieces[56] = Constants.BlackRook;
                board.Pieces[toSquare] = board.Pieces[59] = Constants.None;
                board.Move(Constants.BlackRook, 59, 56);
                board.Move(Constants.BlackKing, toSquare, fromSquare);
                break;
            case Constants.Castle when toSquare == 6:
                board.Pieces[fromSquare] = Constants.WhiteKing;
                board.Pieces[toSquare] = board.Pieces[5] = Constants.None;
                board.Pieces[7] = Constants.WhiteRook;
                board.Move(Constants.WhiteRook, 5, 7);
                board.Move(Constants.WhiteKing, toSquare, fromSquare);
                break;
            case Constants.Castle:
            {
                if (toSquare == 2)
                {
                    board.Pieces[fromSquare] = Constants.WhiteKing;
                    board.Pieces[toSquare] = board.Pieces[3] = Constants.None;
                    board.Pieces[0] = Constants.WhiteRook;
                    board.Move(Constants.WhiteRook, 3, 0);
                    board.Move(Constants.WhiteKing, toSquare, fromSquare);
                }

                break;
            }
            default:
            {
                board.Pieces[fromSquare] = movedPiece;
                board.Pieces[toSquare] = capturedPiece;

                // Normal move
                board.Move(movedPiece, toSquare, fromSquare);

                if (capturedPiece != Constants.None)
                {
                    board.Add(capturedPiece, toSquare);
                }

                break;
            }
        }

        board.CastleRights = prevCastleRights;
        board.Occupancy = board.WhitePieces | board.BlackPieces;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FinishUnApplyMove(this BoardState board, uint m, int oldEnpassantFile)
    {
        var (movedPiece, fromSquare, toSquare, capturedPiece, moveType) = m.Deconstruct();

        if (movedPiece == Constants.WhiteKing && fromSquare.IsMirroredSide() != board.Evaluator.WhiteMirrored)
        {
            board.Evaluator.MirrorWhite(board, fromSquare.IsMirroredSide());
        }
        else if (moveType == Constants.BlackKing && fromSquare.IsMirroredSide() != board.Evaluator.BlackMirrored)
        {
            board.Evaluator.MirrorBlack(board, fromSquare.IsMirroredSide());
        }

        switch (moveType)
        {
            case Constants.EnPassant:
                board.Evaluator.Replace(movedPiece, toSquare, fromSquare);
                board.Evaluator.Apply(capturedPiece, fromSquare.GetRankIndex() * 8 + oldEnpassantFile);
                ++board.PieceCount;
                break;
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

                board.Evaluator.Deactivate(movedPiece + moveType + moveType - 6, toSquare);
                board.Evaluator.Apply(movedPiece, fromSquare);

                if (capturedPiece != Constants.None)
                {
                    board.Evaluator.Apply(capturedPiece, toSquare);
                    ++board.PieceCount;
                }

                break;
            }
            case Constants.Castle when toSquare == 62:
                board.Evaluator.Replace(Constants.BlackRook, 61, 63);
                board.Evaluator.Replace(Constants.BlackKing, toSquare, fromSquare);
                break;
            case Constants.Castle when toSquare == 58:
                board.Evaluator.Replace(Constants.BlackRook, 59, 56);
                board.Evaluator.Replace(Constants.BlackKing, toSquare, fromSquare);
                break;
            case Constants.Castle when toSquare == 6:
                board.Evaluator.Replace(Constants.WhiteRook, 5, 7);
                board.Evaluator.Replace(Constants.WhiteKing, toSquare, fromSquare);
                break;
            case Constants.Castle:
            {
                if (toSquare == 2)
                {
                    board.Evaluator.Replace(Constants.WhiteRook, 3, 0);
                    board.Evaluator.Replace(Constants.WhiteKing, toSquare, fromSquare);
                }

                break;
            }
            case Constants.DoublePush:
                board.Evaluator.Replace(movedPiece, toSquare, fromSquare);
                break;
            default:
            {
                board.Evaluator.Replace(movedPiece, toSquare, fromSquare);

                if (capturedPiece != Constants.None)
                {
                    board.Evaluator.Apply(capturedPiece, toSquare);
                    ++board.PieceCount;
                }

                break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyNullMove(this BoardState board)
    {
        board.Hash ^= Zobrist.SideToMove;

        if (board.EnPassantFile != 8)
        {
            board.Hash ^= Zobrist.EnPassantFile[board.EnPassantFile];
            board.EnPassantFile = 8;
        }

        board.TurnCount++;
        board.WhiteToMove = !board.WhiteToMove;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UnApplyNullMove(this BoardState board, ulong oldHash, byte oldEnpassant)
    {
        board.Hash = oldHash;
        board.EnPassantFile = oldEnpassant;
        board.TurnCount--;
        board.WhiteToMove = !board.WhiteToMove;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Set(this BoardState board, byte piece, int index)
    {
        board.Pieces[index] = piece;
        board.PieceCount++;
        board.Add(piece, (byte)index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSquareOccupied(this BoardState board, ulong position)
    {
        return (board.Occupancy & position) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEmptySquare(this BoardState board, ulong position)
    {
        return (board.Occupancy & position) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasMajorPieces(this BoardState board, bool isWhite)
    {
        if (isWhite)
        {
            return (board.WhitePieces & ~(board.WhitePawns | board.WhiteKings)) > 0;
        }

        return (board.BlackPieces & ~(board.BlackPawns | board.BlackKings)) > 0;
    }

    public static void Apply(this BoardState board, uint move)
    {
        var prevEnpassant = board.EnPassantFile;
        var prevCastleRights = board.CastleRights;

        board.PartialApply(move);
        board.UpdateCheckStatus();
        board.FinishApply(move, prevEnpassant, prevCastleRights);
        board.UpdateRepetitions(move);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Evaluate(this BoardState board)
    {
        return board.Evaluator.Evaluate(board.WhiteToMove, board.PieceCount);
    }
}