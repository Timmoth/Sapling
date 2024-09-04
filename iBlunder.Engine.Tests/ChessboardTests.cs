using FluentAssertions;

namespace iBlunder.Engine.Tests;

public class ChessboardTests
{
    [Fact]
    public void InitialState_Returns_Correct_InitialBoardState()
    {
        var (gameState, searcher) = ChessboardHelpers.InitialState();

        gameState.Board.Pieces.Should().BeEquivalentTo(new[]
        {
            Constants.WhiteRook, Constants.WhiteKnight, Constants.WhiteBishop, Constants.WhiteQueen,
            Constants.WhiteKing,
            Constants.WhiteBishop, Constants.WhiteKnight, Constants.WhiteRook,
            Constants.WhitePawn, Constants.WhitePawn, Constants.WhitePawn, Constants.WhitePawn, Constants.WhitePawn,
            Constants.WhitePawn,
            Constants.WhitePawn, Constants.WhitePawn,
            Constants.None, Constants.None, Constants.None, Constants.None, Constants.None, Constants.None,
            Constants.None, Constants.None,
            Constants.None, Constants.None, Constants.None, Constants.None, Constants.None, Constants.None,
            Constants.None, Constants.None,
            Constants.None, Constants.None, Constants.None, Constants.None, Constants.None, Constants.None,
            Constants.None, Constants.None,
            Constants.None, Constants.None, Constants.None, Constants.None, Constants.None, Constants.None,
            Constants.None, Constants.None,
            Constants.BlackPawn, Constants.BlackPawn, Constants.BlackPawn, Constants.BlackPawn, Constants.BlackPawn,
            Constants.BlackPawn,
            Constants.BlackPawn, Constants.BlackPawn,
            Constants.BlackRook, Constants.BlackKnight, Constants.BlackBishop, Constants.BlackQueen,
            Constants.BlackKing,
            Constants.BlackBishop, Constants.BlackKnight, Constants.BlackRook
        });
        gameState.History.Should().BeEmpty();
    }
}