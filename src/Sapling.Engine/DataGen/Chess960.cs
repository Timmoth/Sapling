namespace Sapling.Engine.DataGen;

using System.Collections.Generic;
using System.Linq;

public static class Chess960
{
    public static string[] Fens;

    static Chess960()
    {
        char[] board = new char[8];
        var fens = new HashSet<string>();
        GenerateBishops(board, fens);
        Fens = fens.ToArray();
    }

    // Step 1: Place bishops on opposite-colored squares
    private static void GenerateBishops(char[] board, HashSet<string> fenList)
    {
        for (int b1 = 0; b1 < 8; b1 += 2)  // Dark square bishop
        {
            for (int b2 = 1; b2 < 8; b2 += 2)  // Light square bishop
            {
                board[b1] = 'B';
                board[b2] = 'B';
                GenerateRooksAndKing(board, fenList);  // Proceed to place rooks and king
                board[b1] = '\0';
                board[b2] = '\0';
            }
        }
    }

    // Step 2: Place the king between the two rooks
    private static void GenerateRooksAndKing(char[] board, HashSet<string> fenList)
    {
        for (int r1 = 0; r1 < 8; r1++)
        {
            if (board[r1] != '\0') continue;  // Skip occupied squares (bishops)

            for (int r2 = r1 + 1; r2 < 8; r2++)
            {
                if (board[r2] != '\0') continue;

                // Ensure the king is placed between the two rooks
                for (int k = r1 + 1; k < r2; k++)
                {
                    if (board[k] == '\0')  // The square must be free for the king
                    {
                        board[r1] = 'R';
                        board[r2] = 'R';
                        board[k] = 'K';
                        GenerateKnightsAndQueen(board, fenList);  // Proceed to place knights and queen
                        board[r1] = '\0';
                        board[r2] = '\0';
                        board[k] = '\0';
                    }
                }
            }
        }
    }

    // Step 3: Place knights and queen in remaining empty squares
    private static void GenerateKnightsAndQueen(char[] board, HashSet<string> fenList)
    {
        List<int> emptySquares = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            if (board[i] == '\0')
            {
                emptySquares.Add(i);
            }
        }

        // There are 3 empty squares remaining, so permute 'N', 'N', 'Q'
        char[] remainingPieces = { 'N', 'N', 'Q' };
        Permute(remainingPieces, 0, board, emptySquares, fenList);
    }

    // Step 4: Permute remaining knights and queen, placing them on the board
    private static void Permute(char[] pieces, int index, char[] board, List<int> emptySquares, HashSet<string> fenList)
    {
        if (index == pieces.Length)
        {
            // Fill the empty squares with the current permutation
            for (int i = 0; i < emptySquares.Count; i++)
            {
                board[emptySquares[i]] = pieces[i];
            }

            // Generate the FEN string with Shredder-style castling rights
            var castlingRights = GetShredderCastlingRights(board);
            var fen = string.Join("", board).ToLower() + "/pppppppp/8/8/8/8/PPPPPPPP/" + string.Join("", board).ToUpper() + " w " + castlingRights.ToUpper()+ castlingRights.ToLower() + " - 0 1";
            fenList.Add(fen);

            // Clear the board for the next permutation
            foreach (var indexToClear in emptySquares)
            {
                board[indexToClear] = '\0';
            }

            return;
        }

        for (int i = index; i < pieces.Length; i++)
        {
            Swap(ref pieces[index], ref pieces[i]);
            Permute(pieces, index + 1, board, emptySquares, fenList);
            Swap(ref pieces[index], ref pieces[i]);  // Backtrack
        }
    }

    // Helper function to swap pieces in the permutation
    private static void Swap(ref char a, ref char b)
    {
        (a, b) = (b, a);
    }

    // Generate castling rights in Shredder FEN format by checking rook positions
    private static string GetShredderCastlingRights(char[] board)
    {
        string castlingRights = "";

        // Find the positions of the rooks (R) and convert them to file letters (a-h)
        for (int i = 0; i < 8; i++)
        {
            if (board[i] == 'R')
            {
                castlingRights += (char)('a' + i);  // Convert index to file letter
            }
        }

        return castlingRights;
    }
}
