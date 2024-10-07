using System.Runtime.CompilerServices;

namespace Sapling.Engine.MoveGen;

public static class MoveExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetMovedPiece(this uint move)
    {
        return (byte)(move & 0x0F);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetCounterMoveIndex(this uint move)
    {
        return ((move & 0x0F) << 6) + ((move >> 10) & 0x3F);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPawn(this uint move)
    {
        uint piece = move & 0x0F; // 0x0F masks the lower 4 bits
        return piece == Constants.WhitePawn || piece == Constants.BlackPawn;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsRook(this uint move)
    {
        uint piece = move & 0x0F; // 0x0F masks the lower 4 bits
        return piece == Constants.WhiteRook || piece == Constants.BlackRook;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKnight(this uint move)
    {
        uint piece = move & 0x0F; // 0x0F masks the lower 4 bits
        return piece == Constants.WhiteKnight || piece == Constants.BlackKnight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBishop(this uint move)
    {
        uint piece = move & 0x0F; // 0x0F masks the lower 4 bits
        return piece == Constants.WhiteBishop || piece == Constants.BlackBishop;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsQueen(this uint move)
    {
        uint piece = move & 0x0F; // 0x0F masks the lower 4 bits
        return piece == Constants.WhiteQueen || piece == Constants.BlackQueen;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKing(this uint move)
    {
        uint piece = move & 0x0F; // 0x0F masks the lower 4 bits
        return piece == Constants.WhiteKing || piece == Constants.BlackKing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEnPassant(this uint move)
    {
        return ((move >> 20) & 0x0F) == Constants.EnPassant;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCastle(this uint move)
    {
        return ((move >> 20) & 0x0F) == Constants.Castle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPromotion(this uint move)
    {
        return ((move >> 20) & 0x0F) >= 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsRookPromotion(this uint move)
    {
        return ((move >> 20) & 0x0F) == Constants.PawnRookPromotion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKnightPromotion(this uint move)
    {
        return ((move >> 20) & 0x0F) == Constants.PawnKnightPromotion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBishopPromotion(this uint move)
    {
        return ((move >> 20) & 0x0F) == Constants.PawnBishopPromotion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsQueenPromotion(this uint move)
    {
        return ((move >> 20) & 0x0F) == Constants.PawnQueenPromotion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetFromSquare(this uint move)
    {
        return (byte)((move >> 4) & 0x3F);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetToSquare(this uint move)
    {
        return (byte)((move >> 10) & 0x3F);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetCapturedPiece(this uint move)
    {
        return (byte)((move >> 16) & 0x0F);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetMoveType(this uint move)
    {
        return (byte)((move >> 20) & 0x0F);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPromotionThreat(this uint move)
    {
        var movedPiece = move.GetMovedPiece();

        if (movedPiece == Constants.WhitePawn)
        {
            return move.GetToSquare().GetRankIndex() >= 5;
        }

        if (movedPiece == Constants.BlackPawn)
        {
            return move.GetToSquare().GetRankIndex() <= 3;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeCastleMove(
        byte movedPiece,
        byte fromSquare,
        byte toSquare)
    {
        return (uint)(movedPiece |
                      (fromSquare << 4) |
                      (toSquare << 10) |
                      (Constants.Castle << 20));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeCaptureMove(
        byte movedPiece,
        byte fromSquare,
        byte capturedPiece,
        byte toSquare)
    {
        return (uint)(movedPiece |
                      (fromSquare << 4) |
                      (toSquare << 10) |
                      (capturedPiece << 16));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeWhiteDoublePushMove(
        byte fromSquare,
        byte toSquare)
    {
        return (uint)(Constants.WhitePawn |
                      (fromSquare << 4) |
                      (toSquare << 10) |
                      (Constants.DoublePush << 20));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeBlackDoublePushMove(
        byte fromSquare,
        byte toSquare)
    {
        return (uint)(Constants.BlackPawn |
                      (fromSquare << 4) |
                      (toSquare << 10) |
                      (Constants.DoublePush << 20));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeCapturePromotionMove(
        byte movedPiece,
        byte fromSquare,
        byte capturedPiece,
        byte toSquare,
        byte moveType)
    {
        return (uint)(movedPiece |
                      (fromSquare << 4) |
                      (toSquare << 10) |
                      (capturedPiece << 16) |
                      (moveType << 20));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodePromotionMove(
        byte movedPiece,
        byte fromSquare,
        byte toSquare,
        byte moveType)
    {
        return (uint)(movedPiece |
                      (fromSquare << 4) |
                      (toSquare << 10) |
                      (moveType << 20));
    }

    private const int whiteEnpassantOffset = 5 * 8;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeWhiteEnpassantMove(
        byte fromSquare,
        byte enpassantFile)
    {
        return (uint)(Constants.WhitePawn |
                      (fromSquare << 4) |
                      ((whiteEnpassantOffset + enpassantFile) << 10) |
                      (Constants.BlackPawn << 16) |
                      (Constants.EnPassant << 20));
    }

    private const int blackEnpassantOffset = 2 * 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeBlackEnpassantMove(
        byte fromSquare,
        byte enpassantFile)
    {
        return (uint)(Constants.BlackPawn |
                      (fromSquare << 4) |
                      ((blackEnpassantOffset + enpassantFile) << 10) |
                      (Constants.WhitePawn << 16) |
                      (Constants.EnPassant << 20));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeNormalMove(
        int movedPiece,
        int fromSquare,
        int toSquare)
    {
        return (uint)(movedPiece |
                      (fromSquare << 4) |
                      (toSquare << 10));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsReset(this uint move)
    {
        return move.IsCapture() || move.IsPawn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCapture(this uint move)
    {
        return ((move >> 16) & 0x0F) != Constants.None;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsQuiet(this uint move)
    {
        // Not a capture or promotion
        return ((move >> 16) & 0x0F) == Constants.None && ((move >> 20) & 0x0F) < 4;
    }
}