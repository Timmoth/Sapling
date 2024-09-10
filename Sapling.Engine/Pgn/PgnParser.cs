using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Pgn;

public static class PgnParser
{
    public static uint Parse(ReadOnlySpan<char> input, IEnumerable<uint> moves)
    {
        var positions = new List<char>();
        var position = 0;
        var token = Lexer.NextToken(input, ref position);
        var positionOnly = true;

        while (token.TokenType != TokenType.Eof)
        {
            switch (token.TokenType)
            {
                case TokenType.Capture:
                    moves = moves.Where(m => m.IsCapture());
                    break;
                case TokenType.Pawn:
                    moves = moves.Where(m => m.IsPawn());
                    break;
                case TokenType.Knight:
                    positionOnly = false;
                    moves = moves.Where(m => m.IsKnight());
                    break;
                case TokenType.Rook:
                    positionOnly = false;
                    moves = moves.Where(m => m.IsRook());
                    break;
                case TokenType.Bishop:
                    positionOnly = false;
                    moves = moves.Where(m => m.IsBishop());
                    break;
                case TokenType.Queen:
                    positionOnly = false;
                    moves = moves.Where(m => m.IsQueen());
                    break;
                case TokenType.King:
                    positionOnly = false;
                    moves = moves.Where(m => m.IsKing());
                    break;
                case TokenType.File:
                case TokenType.Rank:
                    positions.Add(input[token.Start]);
                    break;
                case TokenType.KingSideCastle:
                    positionOnly = false;
                    moves = moves.Where(m => m.IsCastle() && m.GetToSquare() % 8 == 6);
                    break;
                case TokenType.QueenSideCastle:
                    positionOnly = false;
                    moves = moves.Where(m => m.IsCastle() && m.GetToSquare() % 8 == 2);
                    break;
                case TokenType.Promotion:
                {
                    token = Lexer.NextToken(input, ref position);
                    moves = token.TokenType switch
                    {
                        TokenType.Bishop => moves.Where(m => m.IsBishopPromotion()),
                        TokenType.Knight => moves.Where(m => m.IsKnightPromotion()),
                        TokenType.Rook => moves.Where(m => m.IsRookPromotion()),
                        TokenType.Queen => moves.Where(m => m.IsQueenPromotion()),
                        _ => moves
                    };

                    break;
                }
            }

            token = Lexer.NextToken(input, ref position);
        }

        if (positionOnly)
        {
            moves = moves.Where(m => m.IsPawn());
        }

        if (positions.Count <= 0)
        {
            return moves.FirstOrDefault();
        }

        var toSquare = PgnSplitter.GetSquare(positions[^2], positions[^1]);
        moves = moves.Where(m => m.GetToSquare() == toSquare);
        switch (positions.Count)
        {
            case 3:
            {
                var c = positions[0];
                if (char.IsLetter(c))
                {
                    var file = PgnSplitter.GetFile(c);
                    moves = moves.Where(m => m.GetFromSquare() % 8 == file);
                }
                else
                {
                    var rank = PgnSplitter.GetRank(c);
                    moves = moves.Where(m => m.GetFromSquare() / 8 == rank);
                }

                break;
            }
            case 4:
            {
                var fromSquare = PgnSplitter.GetSquare(positions[0], positions[1]);
                moves = moves.Where(m => m.GetFromSquare() == fromSquare);
                break;
            }
        }

        return moves.FirstOrDefault();
    }
}