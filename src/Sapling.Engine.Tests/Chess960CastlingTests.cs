using Sapling.Engine;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

public class Chess960CastlingTests
{
    [Fact]
    public unsafe void Chess960_CastlingAcrossKingAndRookFilesMovesPiecesToStandardDestinations()
    {
        const string files = "abcdefgh";
        var checkedCases = 0;

        foreach (var whiteToMove in new[] { true, false })
        {
            for (var kingFile = 0; kingFile < 8; kingFile++)
            {
                for (var rookFile = 0; rookFile < 8; rookFile++)
                {
                    if (rookFile == kingFile)
                    {
                        continue;
                    }

                    var kingSide = rookFile > kingFile;
                    var fen = CreatePosition(whiteToMove, kingFile, rookFile);
                    using var game = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
                    var castle = Assert.Single(game.LegalMoves, m => m.GetMoveType() == Constants.Castle);
                    var expectedMove = $"{files[kingFile]}{(whiteToMove ? '1' : '8')}{files[rookFile]}{(whiteToMove ? '1' : '8')}";
                    Assert.Equal(expectedMove, castle.ToUciMoveName());

                    game.Apply(castle);

                    var kingDestination = (whiteToMove ? 0 : 56) + (kingSide ? 6 : 2);
                    var rookDestination = (whiteToMove ? 0 : 56) + (kingSide ? 5 : 3);
                    var rookPiece = whiteToMove ? Constants.WhiteRook : Constants.BlackRook;
                    Assert.Equal(kingDestination, whiteToMove ? game.Board.WhiteKingSquare : game.Board.BlackKingSquare);
                    Assert.Equal(1UL << rookDestination, game.Board.Occupancy[rookPiece]);
                    Assert.Equal(CastleRights.None, game.Board.CastleRights);
                    Assert.Equal(Zobrist.CalculateZobristKey(ref game.Board), game.Board.Hash);
                    checkedCases++;
                }
            }
        }

        Assert.Equal(112, checkedCases);
    }

    private static string CreatePosition(bool whiteToMove, int kingFile, int rookFile)
    {
        const string files = "abcdefgh";
        var opponentKingFile = (rookFile + 1) % 8;
        var rankOne = new char[8];
        var rankEight = new char[8];
        Array.Fill(rankOne, '.');
        Array.Fill(rankEight, '.');

        var right = files[rookFile];
        if (whiteToMove)
        {
            rankOne[kingFile] = 'K';
            rankOne[rookFile] = 'R';
            rankEight[opponentKingFile] = 'k';
            right = char.ToUpperInvariant(right);
        }
        else
        {
            rankEight[kingFile] = 'k';
            rankEight[rookFile] = 'r';
            rankOne[opponentKingFile] = 'K';
        }

        return $"{Compress(rankEight)}/8/8/8/8/8/8/{Compress(rankOne)} {(whiteToMove ? 'w' : 'b')} {right} - 0 1";
    }

    private static string Compress(char[] rank)
    {
        var result = new System.Text.StringBuilder();
        var empty = 0;
        foreach (var piece in rank)
        {
            if (piece == '.')
            {
                empty++;
                continue;
            }

            if (empty > 0)
            {
                result.Append(empty);
                empty = 0;
            }

            result.Append(piece);
        }

        if (empty > 0)
        {
            result.Append(empty);
        }

        return result.ToString();
    }
}
