using System.Globalization;
using System.Text;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Pgn;

namespace Sapling.Engine;

public static class OutputHelpers
{
    public static string ToMoveString(this uint move)
    {
        move.Deconstruct(out var movedPiece, out var fromSquare, out var toSquare, out var capturedPiece, out var moveType);
        return
            $"{movedPiece.PieceToChar()} from: {fromSquare} to: {toSquare} cap: {capturedPiece.PieceToChar()} mov: {moveType}";
    }


    public static string ToPgn(this GameState gameState)
    {
        var stringBuilder = new StringBuilder();
        var turnIndex = 1;
        for (var index = 0; index < gameState.History.Count; index += 2)
        {
            stringBuilder.Append(turnIndex++);
            stringBuilder.Append(". ");

            stringBuilder.Append(gameState.History[index].ToPgnMoveName());
            stringBuilder.Append(" ");

            if (index + 1 >= gameState.History.Count)
            {
                break;
            }

            stringBuilder.Append(gameState.History[index + 1].ToPgnMoveName());
            stringBuilder.Append(" ");
        }

        return stringBuilder.ToString();
    }

    public static unsafe string ToFen(this ref BoardStateData board)
    {
        var fen = new StringBuilder();

        for (var row = 7; row >= 0; row--)
        {
            var emptyCount = 0;

            for (var col = 0; col < 8; col++)
            {
                var piece = board.GetPiece(row * 8 + col);
                if (piece == Constants.None)
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        fen.Append(emptyCount);
                        emptyCount = 0;
                    }

                    fen.Append(((Piece)piece).PieceToChar());
                }
            }

            if (emptyCount > 0)
            {
                fen.Append(emptyCount);
            }

            if (row > 0)
            {
                fen.Append('/');
            }
        }

        fen.Append(" ");
        fen.Append(board.WhiteToMove ? "w" : "b");
        fen.Append(" ");

        if (board.CastleRights == CastleRights.None)
        {
            fen.Append("-");
        }
        else
        {
            if (board.CastleRights.HasFlag(CastleRights.WhiteKingSide))
            {
                fen.Append("K");
            }

            if (board.CastleRights.HasFlag(CastleRights.WhiteQueenSide))
            {
                fen.Append("Q");
            }

            if (board.CastleRights.HasFlag(CastleRights.BlackKingSide))
            {
                fen.Append("k");
            }

            if (board.CastleRights.HasFlag(CastleRights.BlackQueenSide))
            {
                fen.Append("q");
            }
        }

        if (board.EnPassantFile >= 8)
        {
            fen.Append(" -");
        }
        else
        {
            fen.Append(" ");
            var enpassantTargetSquare = board.WhiteToMove ? 5 * 8 + board.EnPassantFile : 2 * 8 + board.EnPassantFile;
            fen.Append(((byte)enpassantTargetSquare).ConvertPosition());
        }

        fen.Append(" ");
        fen.Append(board.HalfMoveClock);

        fen.Append(" ");
        fen.Append(board.TurnCount);

        return fen.ToString();
    }
    public static char PieceToChar(this ushort piece)
    {
        return piece switch
        {
            Constants.BlackPawn => 'p',
            Constants.BlackRook => 'r',
            Constants.BlackKnight => 'n',
            Constants.BlackBishop => 'b',
            Constants.BlackQueen => 'q',
            Constants.BlackKing => 'k',
            Constants.WhitePawn => 'P',
            Constants.WhiteRook => 'R',
            Constants.WhiteKnight => 'N',
            Constants.WhiteBishop => 'B',
            Constants.WhiteQueen => 'Q',
            Constants.WhiteKing => 'K',
            _ => '1'
        };
    }
    public static char PieceToChar(this byte piece)
    {
        return piece switch
        {
            Constants.BlackPawn => 'p',
            Constants.BlackRook => 'r',
            Constants.BlackKnight => 'n',
            Constants.BlackBishop => 'b',
            Constants.BlackQueen => 'q',
            Constants.BlackKing => 'k',
            Constants.WhitePawn => 'P',
            Constants.WhiteRook => 'R',
            Constants.WhiteKnight => 'N',
            Constants.WhiteBishop => 'B',
            Constants.WhiteQueen => 'Q',
            Constants.WhiteKing => 'K',
            _ => '1'
        };
    }

    public static char PieceToChar(this Piece piece)
    {
        return piece switch
        {
            Piece.BlackPawn => 'p',
            Piece.BlackRook => 'r',
            Piece.BlackKnight => 'n',
            Piece.BlackBishop => 'b',
            Piece.BlackQueen => 'q',
            Piece.BlackKing => 'k',
            Piece.WhitePawn => 'P',
            Piece.WhiteRook => 'R',
            Piece.WhiteKnight => 'N',
            Piece.WhiteBishop => 'B',
            Piece.WhiteQueen => 'Q',
            Piece.WhiteKing => 'K',
            _ => '1'
        };
    }

    public static char PieceToUpperChar(this Piece piece)
    {
        return piece switch
        {
            Piece.BlackPawn => 'P',
            Piece.BlackRook => 'R',
            Piece.BlackKnight => 'N',
            Piece.BlackBishop => 'B',
            Piece.BlackQueen => 'Q',
            Piece.BlackKing => 'K',
            Piece.WhitePawn => 'P',
            Piece.WhiteRook => 'R',
            Piece.WhiteKnight => 'N',
            Piece.WhiteBishop => 'B',
            Piece.WhiteQueen => 'Q',
            Piece.WhiteKing => 'K',
            _ => '1'
        };
    }

    public static Piece CharToPiece(this char c)
    {
        return c switch
        {
            'p' => Piece.BlackPawn,
            'r' => Piece.BlackRook,
            'n' => Piece.BlackKnight,
            'b' => Piece.BlackBishop,
            'q' => Piece.BlackQueen,
            'k' => Piece.BlackKing,
            'P' => Piece.WhitePawn,
            'R' => Piece.WhiteRook,
            'N' => Piece.WhiteKnight,
            'B' => Piece.WhiteBishop,
            'Q' => Piece.WhiteQueen,
            'K' => Piece.WhiteKing,
            _ => Piece.None
        };
    }

    public static unsafe string CreateDiagram(this GameState gameState, bool blackAtTop = true, bool includeFen = true,
        bool includeZobristKey = true)
    {
        StringBuilder output = new();
        var lastMoveSquare = gameState.History.Count > 0 ? (int)gameState.History[^1].GetToSquare() : -1;

        for (var y = 0; y < 8; y++)
        {
            var rankIndex = blackAtTop ? 7 - y : y;
            output.AppendLine("+---+---+---+---+---+---+---+---+");

            for (var x = 0; x < 8; x++)
            {
                var fileIndex = blackAtTop ? x : 7 - x;
                var squareIndex = rankIndex * 8 + fileIndex;
                var highlight = squareIndex == lastMoveSquare;
                var piece = gameState.Board.GetPiece(squareIndex);
                if (piece != 0)
                {
                    if (highlight)
                    {
                        output.Append($"|({((Piece)piece).PieceToChar()})");
                    }
                    else
                    {
                        output.Append($"| {((Piece)piece).PieceToChar()} ");
                    }
                }
                else
                {
                    output.Append("|   ");
                }

                if (x == 7)
                {
                    output.AppendLine($"| {rankIndex + 1}");
                }
            }

            if (y != 7)
            {
                continue;
            }

            // Show file names
            output.AppendLine("+---+---+---+---+---+---+---+---+");
            const string fileNames = "  a   b   c   d   e   f   g   h  ";
            const string fileNamesRev = "  h   g   f   e   d   c   b   a  ";
            output.AppendLine(blackAtTop ? fileNames : fileNamesRev);
            output.AppendLine();

            if (includeFen)
            {
                output.AppendLine($"Fen         : {gameState.Board.ToFen()}");
            }

            if (includeZobristKey)
            {
                output.AppendLine($"Zobrist Key : {gameState.Board.Hash.ToString("X")}");
            }
        }

        return output.ToString();
    }

    public static unsafe string CreateDiagram(this ref BoardStateData board, bool blackAtTop = true, bool includeFen = true,
        bool includeZobristKey = true)
    {
        StringBuilder output = new();

        for (var y = 0; y < 8; y++)
        {
            var rankIndex = blackAtTop ? 7 - y : y;
            output.AppendLine("+---+---+---+---+---+---+---+---+");

            for (var x = 0; x < 8; x++)
            {
                var fileIndex = blackAtTop ? x : 7 - x;
                var squareIndex = rankIndex * 8 + fileIndex;
                var piece = board.GetPiece(squareIndex);
                if (piece == 0)
                {
                    output.Append("|   ");
                }
                else
                {
                    output.Append($"| {((Piece)piece).PieceToChar()} ");
                }

                if (x == 7)
                {
                    output.AppendLine($"| {rankIndex + 1}");
                }
            }

            if (y != 7)
            {
                continue;
            }

            // Show file names
            output.AppendLine("+---+---+---+---+---+---+---+---+");
            const string fileNames = "  a   b   c   d   e   f   g   h  ";
            const string fileNamesRev = "  h   g   f   e   d   c   b   a  ";
            output.AppendLine(blackAtTop ? fileNames : fileNamesRev);
            output.AppendLine();

            if (includeFen)
            {
                output.AppendLine($"Fen         : {board.ToFen()}");
            }

            if (includeZobristKey)
            {
                output.AppendLine($"Zobrist Key : {board.Hash}");
            }
        }

        return output.ToString();
    }

    public static string ToUciMoveName(this uint move)
    {
        var promotion = "";

        var moveType = move.GetMoveType();

        if (moveType == Constants.PawnRookPromotion)
        {
            promotion += "r";
        }
        else if (moveType == Constants.PawnKnightPromotion)
        {
            promotion += "n";
        }
        else if (moveType == Constants.PawnBishopPromotion)
        {
            promotion += "b";
        }
        else if (moveType == Constants.PawnQueenPromotion)
        {
            promotion += "q";
        }

        return
            $"{((byte)move.GetFromSquare()).ConvertPosition()}{((byte)move.GetToSquare()).ConvertPosition()}{promotion}";
    }

    public static string ToPgnMoveName(this uint move)
    {
        var moveType = move.GetMoveType();
        var fromSquare = move.GetFromSquare();
        var movedPiece = move.GetMovedPiece();
        var toSquare = move.GetToSquare();

        if (moveType == Constants.Castle)
        {
            return toSquare % 8 == 2 ? "O-O-O" : "O-O";
        }

        var stringBuilder = new StringBuilder();

        stringBuilder.Append(((Piece)movedPiece).PieceToUpperChar());
        stringBuilder.Append(PgnSplitter.ConvertPosition(((byte)fromSquare)));
        if (move.IsCapture())
        {
            stringBuilder.Append("x");
        }

        stringBuilder.Append(PgnSplitter.ConvertPosition((byte)move.GetToSquare()));

        if (move.GetMoveType() == Constants.PawnRookPromotion)
        {
            stringBuilder.Append("=R");
        }
        else if (move.GetMoveType() == Constants.PawnKnightPromotion)
        {
            stringBuilder.Append("=N");
        }
        else if (move.GetMoveType() == Constants.PawnBishopPromotion)
        {
            stringBuilder.Append("=B");
        }
        else if (move.GetMoveType() == Constants.PawnQueenPromotion)
        {
            stringBuilder.Append("=Q");
        }

        return stringBuilder.ToString();
    }

    public static string FormatMilliseconds(this float milliseconds)
    {
        if (milliseconds >= 1000)
        {
            // Convert to seconds if greater than or equal to 1000 milliseconds
            var seconds = milliseconds / 1000;
            return $"{seconds:F2}s"; // Formatting to 2 decimal places
        }

        if (milliseconds >= 1)
        {
            // Keep as milliseconds if between 1 and 999 milliseconds
            return $"{milliseconds:F2}ms"; // Formatting to 2 decimal places
        }

        if (milliseconds >= 0.001)
        {
            // Convert to microseconds if between 1 millisecond and 999 microseconds
            var microseconds = milliseconds * 1000;
            return $"{microseconds:F2}µs"; // Formatting to 2 decimal places
        }

        // Convert to nanoseconds if less than 1 microsecond
        var nanoseconds = milliseconds * 1_000_000;
        return $"{nanoseconds:F2}ns"; // Formatting to 2 decimal places
    }

    public static string FormatBigNumber(this long number)
    {
        if (number >= 1000000000)
        {
            return (number / 1000000000D).ToString("0.#") + "b";
        }

        if (number >= 1000000)
        {
            return (number / 1000000D).ToString("0.#") + "m";
        }

        if (number >= 1000)
        {
            return (number / 1000D).ToString("0.#") + "k";
        }

        return number.ToString();
    }

    public static string FormatBigNumber(this double number)
    {
        if (number >= 1000000000)
        {
            return (number / 1000000000D).ToString("0.#") + "b";
        }

        if (number >= 1000000)
        {
            return (number / 1000000D).ToString("0.#") + "m";
        }

        if (number >= 1000)
        {
            return (number / 1000D).ToString("0.#") + "k";
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatBigNumber(this ulong number)
    {
        if (number >= 1000000000)
        {
            return (number / 1000000000D).ToString("0.#") + "b";
        }

        if (number >= 1000000)
        {
            return (number / 1000000D).ToString("0.#") + "m";
        }

        if (number >= 1000)
        {
            return (number / 1000D).ToString("0.#") + "k";
        }

        return number.ToString();
    }

    public static string FormatBigNumber(this int number)
    {
        if (number >= 1000000000)
        {
            return (number / 1000000000D).ToString("0.#") + "b";
        }

        if (number >= 1000000)
        {
            return (number / 1000000D).ToString("0.#") + "m";
        }

        if (number >= 1000)
        {
            return (number / 1000D).ToString("0.#") + "k";
        }

        return number.ToString();
    }

    public static float RoundToSignificantFigures(this double number, int significantFigures)
    {
        if (number == 0)
        {
            return 0;
        }

        var scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(number))) + 1);
        return (float)(scale * Math.Round(number / scale, significantFigures));
    }
}