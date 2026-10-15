namespace Sapling.Engine;

/// <summary>
/// Syntax validation for FEN strings. The parser is strict (it throws) so callers that must keep
/// running - the UCI command loop above all - validate first and reject the command instead of
/// letting a malformed position take down the process.
/// </summary>
public static class FenValidator
{
    private const string ValidPieces = "pnbrqkPNBRQK";

    /// <summary>Returns null when the FEN is usable, otherwise a short reason for the rejection.</summary>
    public static string? Validate(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
            return "FEN is empty.";

        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return $"FEN needs at least 4 fields, got {parts.Length}.";
        if (parts.Length > 6)
            return $"FEN has too many fields ({parts.Length}).";

        if (ValidateBoard(parts[0]) is { } boardError)
            return boardError;

        if (parts[1] != "w" && parts[1] != "b")
            return "FEN side to move must be 'w' or 'b'.";

        if (ValidateCastlingRights(parts[2]) is { } rightsError)
            return rightsError;

        if (ValidateEnPassant(parts[3]) is { } enPassantError)
            return enPassantError;

        if (parts.Length > 4 && (!ushort.TryParse(parts[4], out var halfMoveClock) || halfMoveClock > byte.MaxValue))
            return $"FEN halfmove clock must be a number between 0 and {byte.MaxValue}.";

        if (parts.Length > 5 && (!ushort.TryParse(parts[5], out var fullMoveNumber) || fullMoveNumber == 0))
            return "FEN fullmove number must be a positive number.";

        return null;
    }

    private static string? ValidateBoard(string board)
    {
        var rows = board.Split('/');
        if (rows.Length != 8)
            return $"FEN board must have 8 ranks, got {rows.Length}.";

        var whiteKings = 0;
        var blackKings = 0;
        var whitePawns = 0;
        var blackPawns = 0;
        var whitePieces = 0;
        var blackPieces = 0;

        foreach (var row in rows)
        {
            if (row.Length == 0)
                return "FEN rank must not be empty.";

            var squares = 0;
            foreach (var c in row)
            {
                if (char.IsDigit(c))
                {
                    squares += c - '0';
                }
                else if (ValidPieces.Contains(c))
                {
                    squares++;
                    switch (c)
                    {
                        case 'K': whiteKings++; whitePieces++; break;
                        case 'k': blackKings++; blackPieces++; break;
                        case 'P': whitePawns++; whitePieces++; break;
                        case 'p': blackPawns++; blackPieces++; break;
                        case 'Q' or 'R' or 'B' or 'N': whitePieces++; break;
                        default: blackPieces++; break; // 'q' 'r' 'b' 'n'
                    }
                }
                else
                {
                    return $"FEN board contains the unknown character '{c}'.";
                }

                if (squares > 8)
                    return "FEN rank covers more than 8 squares.";
            }

            if (squares != 8)
                return "Each FEN rank must cover exactly 8 squares.";
        }

        // Unsafe movegen/evaluation (NNUE output buckets, pointer arithmetic) assumes a legal
        // piece census. Enforce the invariants before a position can reach those paths.
        if (whiteKings != 1)
            return $"FEN must have exactly one white king, got {whiteKings}.";
        if (blackKings != 1)
            return $"FEN must have exactly one black king, got {blackKings}.";
        if (whitePawns > 8 || blackPawns > 8)
            return "FEN has more than 8 pawns for one side.";
        if (whitePieces > 16 || blackPieces > 16)
            return "FEN has more than 16 pieces for one side.";

        return null;
    }

    private static string? ValidateCastlingRights(string rights)
    {
        if (rights == "-" || rights.Length == 0)
            return null;

        var standard = false;
        var shredder = false;
        foreach (var c in rights)
        {
            if (c is 'K' or 'Q' or 'k' or 'q')
                standard = true;
            else if (c is >= 'A' and <= 'H' or >= 'a' and <= 'h')
                shredder = true;
            else
                return $"FEN castling rights contain the unknown character '{c}'.";
        }

        if (standard && shredder)
            return "FEN castling rights must not mix standard and Shredder notation.";

        for (var i = 0; i < rights.Length; i++)
        {
            if (rights.AsSpan(i + 1).IndexOf(rights[i]) >= 0)
                return $"FEN castling rights repeat '{rights[i]}'.";
        }

        return null;
    }

    private static string? ValidateEnPassant(string enPassant)
    {
        if (enPassant == "-")
            return null;

        if (enPassant.Length != 2 || enPassant[0] is < 'a' or > 'h' || enPassant[1] is < '3' or > '6')
            return "FEN en passant target must be '-' or a square on ranks 3-6.";

        return null;
    }
}
