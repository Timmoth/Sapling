using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

public class InsufficientMaterialTests
{
    // This helper recognizes material configurations where no legal sequence can
    // result in checkmate. It is intentionally conservative and does not detect
    // every dead position. It does not mean the side to move cannot force a draw.
    [Theory]
    [InlineData("7K/8/8/8/8/2p5/3P4/7k b - - 0 1", false)]
    [InlineData("7K/8/8/8/8/8/8/7k w - - 0 1", true)]
    [InlineData("7K/8/5n2/6N1/8/8/8/7k w - - 0 1", false)]
    [InlineData("7K/8/5n2/6N1/4B3/8/8/7k w - - 0 1", false)]
    [InlineData("7K/8/6B1/8/8/8/8/7k w - - 0 1", true)]
    [InlineData("7K/4b3/6B1/8/8/8/8/7k w - - 0 1", false)]
    [InlineData("7K/8/4N3/5N2/8/8/8/7k w - - 0 1", false)]
    [InlineData("8/8/8/7K/8/3k4/8/8 w - - 0 157", true)]
    [InlineData("5B2/8/8/8/2K5/8/8/2k5 b - - 0 176", true)]
    [InlineData("8/b7/8/8/7K/8/2k5/8 w - - 0 57", true)]
    [InlineData("8/8/8/8/8/6k1/NK6/8 b - - 0 96", true)]
    public void ReportsDeadPositionAccordingToPossibleMate(string fen, bool isDeadPosition)
    {
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        Assert.Equal(isDeadPosition, board.InsufficientMatingMaterial());
    }

    [Theory]
    [InlineData("7K/8/8/8/8/8/8/7k w - - 0 1", true)]
    [InlineData("7K/8/5n2/6N1/8/8/8/7k w - - 0 1", false)]
    [InlineData("7K/8/4N3/5N2/8/8/8/7k w - - 0 1", false)]
    public void GameOver_UsesDeadMaterialClassification(string fen, bool expectedGameOver)
    {
        using var game = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));

        Assert.Equal(expectedGameOver, game.GameOver());
    }

    [Theory]
    [InlineData("k7/8/8/8/1p6/B7/8/7K w - - 0 1", "a3b4")]
    [InlineData("7k/8/b7/1P6/8/8/8/K7 b - - 0 1", "a6b5")]
    public void CapturingLastPawn_UpdatesPieceCountAndRecognizesDeadMaterial(string fen, string uci)
    {
        using var game = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
        Assert.Equal(4, game.Board.PieceCount);
        Assert.False(game.GameOver());

        var capture = game.LegalMoves.Single(move => move.ToUciMoveName() == uci);
        game.Apply(capture);

        Assert.Equal(3, game.Board.PieceCount);
        Assert.True(game.Board.InsufficientMatingMaterial());
        Assert.True(game.GameOver());
    }
}
