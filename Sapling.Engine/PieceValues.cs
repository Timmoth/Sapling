using System.Runtime.InteropServices;

namespace Sapling.Engine;

public static unsafe class PieceValues
{
    public static readonly short* PieceValue;

    static PieceValues()
    {
        PieceValue = Allocate(13);

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

    public static short* Allocate(int count)
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc(sizeof(short) * (nuint)count, alignment);
        NativeMemory.Clear(block, sizeof(short) * (nuint)count);

        return (short*)block;
    }
}