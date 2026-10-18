using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

public class MoveTests
{
    [Theory]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.Normal)]
    [InlineData(Piece.WhiteKing, 0, Piece.None, 0, MoveType.Normal)]
    [InlineData(Piece.None, 2, Piece.None, 0, MoveType.Normal)]
    [InlineData(Piece.None, 0, Piece.BlackKing, 0, MoveType.Normal)]
    [InlineData(Piece.None, 0, Piece.None, 4, MoveType.Normal)]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.EnPassant)]
    [InlineData(Piece.WhiteKing, 2, Piece.BlackKnight, 3, MoveType.EnPassant)]
    [InlineData(Piece.WhiteKing, 2, Piece.BlackKnight, 3, MoveType.PawnBishopPromotion)]
    public void GetKingPosition_Returns_CorrectPosition(Piece movedPiece, byte fromSquare, Piece capturedPiece,
        byte toSquare, MoveType moveType)
    {
        var move = MoveExtensions.EncodeCapturePromotionMove((byte)movedPiece, fromSquare, (byte)capturedPiece,
            toSquare,
            (byte)moveType);
        Assert.Equal((byte)movedPiece, move.GetMovedPiece());
        Assert.Equal(fromSquare, move.GetFromSquare());
        Assert.Equal((byte)capturedPiece, move.GetCapturedPiece());
        Assert.Equal(toSquare, move.GetToSquare());
        Assert.Equal((byte)moveType, move.GetMoveType());
    }

    [Theory]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.Normal, true)]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.Castle, true)]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.EnPassant, true)]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.DoublePush, true)]
    [InlineData(Piece.WhiteQueen, 0, Piece.BlackQueen, 0, MoveType.Normal, false)]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.PawnBishopPromotion, false)]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.PawnRookPromotion, false)]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.PawnKnightPromotion, false)]
    [InlineData(Piece.None, 0, Piece.None, 0, MoveType.PawnQueenPromotion, false)]
    [InlineData(Piece.WhitePawn, 0, Piece.BlackPawn, 0, MoveType.PawnQueenPromotion, false)]
    public void Is_Quiet(Piece movedPiece, byte fromSquare, Piece capturedPiece, byte toSquare, MoveType moveType,
        bool expected)
    {
        var move = MoveExtensions.EncodeCapturePromotionMove((byte)movedPiece, fromSquare, (byte)capturedPiece,
            toSquare,
            (byte)moveType);
        Assert.Equal(expected, move.IsQuiet());
    }
}