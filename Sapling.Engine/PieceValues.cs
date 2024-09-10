namespace Sapling.Engine;

public static class PieceValues
{
    public static readonly short[] PieceValue;

    static PieceValues()
    {
        PieceValue = new short[13];

        PieceValue[Constants.WhitePawn] = Constants.PawnValue;
        PieceValue[Constants.WhiteKnight] = Constants.KnightValue;
        PieceValue[Constants.WhiteBishop] = Constants.BishopValue;
        PieceValue[Constants.WhiteRook] = Constants.RookValue;
        PieceValue[Constants.WhiteQueen] = Constants.QueenValue;
        PieceValue[Constants.WhiteKing] = Constants.KingValue;

        PieceValue[Constants.BlackPawn] = Constants.PawnValue;
        PieceValue[Constants.BlackKnight] = Constants.KnightValue;
        PieceValue[Constants.BlackBishop] = Constants.BishopValue;
        PieceValue[Constants.BlackRook] = Constants.RookValue;
        PieceValue[Constants.BlackQueen] = Constants.QueenValue;
        PieceValue[Constants.BlackKing] = Constants.KingValue;
    }
}