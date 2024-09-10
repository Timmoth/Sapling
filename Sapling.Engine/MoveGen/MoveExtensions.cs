using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace Sapling.Engine.MoveGen;

public static class MoveExtensions
{
    public static ulong[] WhitePassedPawnMasks = new ulong[64];
    public static ulong[] BlackPassedPawnMasks = new ulong[64];
    public static ulong[] WhiteDoubledPawnMasks = new ulong[64];
    public static ulong[] BlackDoubledPawnMasks = new ulong[64];

    static MoveExtensions()
    {
        for (var square = 0; square < 64; square++)
        {
            WhitePassedPawnMasks[square] = GetWhitePassedPawnMask(square);
            BlackPassedPawnMasks[square] = GetBlackPassedPawnMask(square);
            WhiteDoubledPawnMasks[square] = GetWhiteDoubledPawnMask(square);
            BlackDoubledPawnMasks[square] = GetBlackDoubledPawnMask(square);
        }
    }

    private static ulong GetWhitePassedPawnMask(int square)
    {
        var mask = 0UL;
        var rank = square / 8;
        var file = square % 8;

        // Determine mask for white pawns
        for (var r = rank + 1; r < 8; r++)
        {
            if (file > 0)
            {
                mask |= 1UL << (r * 8 + (file - 1)); // left file
            }

            mask |= 1UL << (r * 8 + file); // same file
            if (file < 7)
            {
                mask |= 1UL << (r * 8 + file + 1); // right file
            }
        }

        return mask;
    }

    private static ulong GetBlackPassedPawnMask(int square)
    {
        var mask = 0UL;
        var rank = square / 8;
        var file = square % 8;

        // Determine mask for black pawns
        for (var r = rank - 1; r >= 0; r--)
        {
            if (file > 0)
            {
                mask |= 1UL << (r * 8 + (file - 1)); // left file
            }

            mask |= 1UL << (r * 8 + file); // same file
            if (file < 7)
            {
                mask |= 1UL << (r * 8 + file + 1); // right file
            }
        }

        return mask;
    }

    private static ulong GetWhiteDoubledPawnMask(int square)
    {
        var mask = 0UL;
        var rank = square / 8;
        var file = square % 8;

        for (var r = rank + 1; r < 8; r++) // All ranks above the current square
        {
            mask |= 1UL << (r * 8 + file);
        }

        return mask;
    }

    private static ulong GetBlackDoubledPawnMask(int square)
    {
        var mask = 0UL;
        var rank = square / 8;
        var file = square % 8;

        for (var r = rank - 1; r >= 0; r--) // All ranks below the current square
        {
            mask |= 1UL << (r * 8 + file);
        }

        return mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (byte movedPiece, byte fromSquare, byte toSquare, byte capturedPiece, byte moveType) Deconstruct(
        this uint move)
    {
        return (BitFieldExtract(move, 0, 4),
            BitFieldExtract(move, 4, 6),
            BitFieldExtract(move, 10, 6),
            BitFieldExtract(move, 16, 4),
            BitFieldExtract(move, 20, 4));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte BitFieldExtract(uint bits, byte start, byte length)
    {
        return (byte)Bmi1.X64.BitFieldExtract(bits, start, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetMovedPiece(this uint move)
    {
        return BitFieldExtract(move, 0, 4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPawn(this uint move)
    {
        return move.GetMovedPiece() is Constants.WhitePawn or Constants.BlackPawn;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsRook(this uint move)
    {
        return move.GetMovedPiece() is Constants.WhiteRook or Constants.BlackRook;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKnight(this uint move)
    {
        return move.GetMovedPiece() is Constants.WhiteKnight or Constants.BlackKnight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBishop(this uint move)
    {
        return move.GetMovedPiece() is Constants.WhiteBishop or Constants.BlackBishop;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsQueen(this uint move)
    {
        return move.GetMovedPiece() is Constants.WhiteQueen or Constants.BlackQueen;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKing(this uint move)
    {
        return move.GetMovedPiece() is Constants.WhiteKing or Constants.BlackKing;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEnPassant(this uint move)
    {
        return BitFieldExtract(move, 20, 4) == Constants.EnPassant;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCastle(this uint move)
    {
        return BitFieldExtract(move, 20, 4) == Constants.Castle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPromotion(this uint move)
    {
        return BitFieldExtract(move, 20, 4) >= 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsRookPromotion(this uint move)
    {
        return BitFieldExtract(move, 20, 4) == Constants.PawnRookPromotion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKnightPromotion(this uint move)
    {
        return BitFieldExtract(move, 20, 4) == Constants.PawnKnightPromotion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBishopPromotion(this uint move)
    {
        return BitFieldExtract(move, 20, 4) == Constants.PawnBishopPromotion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsQueenPromotion(this uint move)
    {
        return BitFieldExtract(move, 20, 4) == Constants.PawnQueenPromotion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetFromSquare(this uint move)
    {
        return BitFieldExtract(move, 4, 6);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetToSquare(this uint move)
    {
        return BitFieldExtract(move, 10, 6);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetCapturedPiece(this uint move)
    {
        return BitFieldExtract(move, 16, 4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetMoveType(this uint move)
    {
        return BitFieldExtract(move, 20, 4);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeWhiteEnpassantMove(
        byte fromSquare,
        byte enpassantFile)
    {
        return (uint)(Constants.WhitePawn |
                      (fromSquare << 4) |
                      ((5 * 8 + enpassantFile) << 10) |
                      (Constants.BlackPawn << 16) |
                      (Constants.EnPassant << 20));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeBlackEnpassantMove(
        byte fromSquare,
        byte enpassantFile)
    {
        return (uint)(Constants.BlackPawn |
                      (fromSquare << 4) |
                      ((2 * 8 + enpassantFile) << 10) |
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

    public static void AppendBytes(this uint move, byte[] bytes, int index)
    {
        var from = Position.FromIndex(move.GetFromSquare());
        var to = Position.FromIndex(move.GetToSquare());
        bytes[index] = move.GetMovedPiece();
        bytes[index + 1] = (byte)from.Row;
        bytes[index + 2] = (byte)from.Col;
        bytes[index + 3] = (byte)to.Row;
        bytes[index + 4] = (byte)to.Col;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsReset(this uint move)
    {
        return move.IsCapture() || move.GetMovedPiece() is Constants.WhitePawn or Constants.BlackPawn;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCapture(this uint move)
    {
        return move.GetCapturedPiece() != Constants.None;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsQuiet(this uint move)
    {
        return move.GetCapturedPiece() == Constants.None && BitFieldExtract(move, 20, 4) < 4;
    }
}