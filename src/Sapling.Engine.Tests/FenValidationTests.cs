using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

/// <summary>
/// malformed FENs are a rejected command, not a fatal error. The validator is the gate the
/// UCI layer uses before it touches the live position.
/// </summary>
public class FenValidationTests
{
    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 0")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 65536")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 -1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 300 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w -")]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("4k3/8/8/8/8/8/8/4 w - - 0 1")]
    [InlineData("9/8/8/8/8/8/8/4K3 w - - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 x - - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w Kkqk - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w KQkqK - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w KQkq e9 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w KQkq - 0 1 extra")]
    public void InvalidFens_AreRejectedWithAReason(string fen)
    {
        Assert.NotNull(FenValidator.Validate(fen));
    }

    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 65535")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 100")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w Kkq - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w BGbg - 0 1")]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1")]
    [InlineData("r3k2r/1bp2pP1/5n2/1P1Q4/1pPq4/5N2/1B1P2p1/R1R1K2R b KQkq c3 0 1")]
    public void ValidFens_AreAccepted(string fen)
    {
        Assert.Null(FenValidator.Validate(fen));
    }

    [Fact]
    public void Parser_StillThrowsForFullmoveZero_AsABackstop()
    {
        Assert.Throws<FormatException>(
            () => BoardStateExtensions.CreateBoardFromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 0"));
    }

    [Fact]
    public void HighestUsableFullmoveNumber_StillProducesALegalPosition()
    {
        var board = BoardStateExtensions.CreateBoardFromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 65535");

        Assert.Equal(ushort.MaxValue, board.TurnCount);
        var moves = new List<uint>();
        board.GenerateLegalMoves(moves, false);
        Assert.NotEmpty(moves);
    }
}
