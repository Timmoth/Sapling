using System.Text.RegularExpressions;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Pgn;

public static class PgnSplitter
{
    public const string FileNames = "abcdefgh";
    public const string RankNames = "87654321";

    public static IEnumerable<string> SplitPgnIntoMoves(string pgn)
    {
        // Define the regex pattern to match move numbers (e.g., "1.", "2.", etc.)
        var pattern = @"\d+\.\s?";
        // Use regex to split the PGN string by move numbers
        var splitPgn = Regex.Split(pgn, pattern);


        // Loop through the split parts and combine them with the respective move numbers
        for (var i = 1; i < splitPgn.Length; i++)
        {
            yield return splitPgn[i].Trim();
        }
    }

    public static string ConvertPosition(byte position)
    {
        var rank = position.GetRankIndex();
        var file = position.GetFileIndex();
        return $"{(char)('a' + file)}{(char)('1' + rank)}";
    }

    public static (int file, int rank) GetPosition(this string name)
    {
        return (FileNames.IndexOf(name[0]), RankNames.IndexOf(name[1]));
    }

    public static byte GetSquare(char file, char rank)
    {
        return (byte)(RankNames.IndexOf(rank) * 8 + FileNames.IndexOf(file));
    }

    public static byte GetRank(char rank)
    {
        return (byte)RankNames.IndexOf(rank);
    }

    public static byte GetFile(char file)
    {
        return (byte)FileNames.IndexOf(file);
    }

    public static (int start, int target, MoveType flag) GetMoveFromUCIName(string moveName, Piece[] board)
    {
        var (sFile, sRank) = GetPosition(moveName.Substring(0, 2));
        var startSquare = sRank * 8 + sFile;
        var (tFile, tRank) = GetPosition(moveName.Substring(2, 2));
        var targetSquare = tRank * 8 + tFile;

        var movedPieceType = board[startSquare];

        // Figure out move flag
        var flag = MoveType.Normal;

        if (movedPieceType is Piece.WhitePawn or Piece.BlackPawn)
        {
            // Promotion
            if (moveName.Length > 4)
            {
                flag = moveName[^1] switch
                {
                    'q' => MoveType.PawnQueenPromotion,
                    'r' => MoveType.PawnRookPromotion,
                    'n' => MoveType.PawnKnightPromotion,
                    'b' => MoveType.PawnBishopPromotion,
                    _ => MoveType.Normal
                };
            }
            // Double pawn push
            else if (Math.Abs(tRank - sRank) == 2)
            {
                flag = MoveType.DoublePush;
            }
        }
        else if (movedPieceType is Piece.WhiteKing or Piece.BlackKing)
        {
            if (Math.Abs(sFile - tFile) > 1)
            {
                flag = MoveType.Castle;
            }
        }

        return (startSquare, targetSquare, flag);
    }
}