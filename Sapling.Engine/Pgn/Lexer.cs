namespace Sapling.Engine.Pgn;

public static class Lexer
{
    public const string QueenSideCastleKeyword = "O-O-O";
    public const string KingSideCastleKeyword = "O-O";

    internal static TokenType? MapToken(char token)
    {
        return token switch
        {
            '1' => TokenType.Rank,
            '2' => TokenType.Rank,
            '3' => TokenType.Rank,
            '4' => TokenType.Rank,
            '5' => TokenType.Rank,
            '6' => TokenType.Rank,
            '7' => TokenType.Rank,
            '8' => TokenType.Rank,
            'a' => TokenType.File,
            'b' => TokenType.File,
            'c' => TokenType.File,
            'd' => TokenType.File,
            'e' => TokenType.File,
            'f' => TokenType.File,
            'g' => TokenType.File,
            'h' => TokenType.File,
            'x' => TokenType.Capture,
            '+' => TokenType.Check,
            'P' => TokenType.Pawn,
            'N' => TokenType.Knight,
            'R' => TokenType.Rook,
            'B' => TokenType.Bishop,
            'Q' => TokenType.Queen,
            'K' => TokenType.King,
            '=' => TokenType.Promotion,
            '#' => TokenType.Checkmate,
            '\n' => TokenType.NewLine,
            _ => null
        };
    }

    internal static TokenType MapKeyword(ReadOnlySpan<char> token)
    {
        if (token.SequenceEqual(QueenSideCastleKeyword.AsSpan()))
        {
            return TokenType.QueenSideCastle;
        }

        if (token.SequenceEqual(KingSideCastleKeyword.AsSpan()))
        {
            return TokenType.KingSideCastle;
        }

        throw new Exception("Unrecognized keyword");
    }

    public static Token NextToken(ReadOnlySpan<char> input, ref int position)
    {
        if (position >= input.Length)
        {
            // End of file
            return new Token
            {
                Start = -1,
                Length = -1,
                TokenType = TokenType.Eof
            };
        }

        if (input[position] == ' ')
        {
            // Ignore all white space
            while (position < input.Length && input[position] == ' ')
                // Advance until current character is no longer white space
            {
                position++;
            }

            if (position >= input.Length)
            {
                // End of file
                return new Token
                {
                    Start = -1,
                    Length = -1,
                    TokenType = TokenType.Eof
                };
            }
        }

        // Next token must be a single character token
        var token = MapToken(input[position]);

        if (token != null)
        {
            position++;
            return new Token
            {
                Start = position - 1,
                Length = 1,
                TokenType = token.Value
            };
        }

        var start = position;
        while (position < input.Length && input[position] is 'O' or '-')
            // Advance until the end of the key word
        {
            position++;
        }

        if (position == start)
        {
            return new Token
            {
                Start = -1,
                Length = -1,
                TokenType = TokenType.Eof
            };
        }

        return new Token
        {
            Start = start,
            Length = position - start,
            TokenType = MapKeyword(input.Slice(start, position - start))
        };
    }
}