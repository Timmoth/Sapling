using System.Runtime.CompilerServices;

namespace iBlunder.Engine;

public static class SquareHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetFileIndex(this int square)
    {
        // File is the last 3 bits of the square index
        return square & 7; // Equivalent to square % 8
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetRankIndex(this int square)
    {
        // Rank is obtained by shifting right by 3 bits
        return square >> 3; // Equivalent to square / 8
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetFileIndex(this byte square)
    {
        // File is the last 3 bits of the square index
        return (byte)(square & 7); // Equivalent to square % 8
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetRankIndex(this byte square)
    {
        // Rank is obtained by shifting right by 3 bits
        return (byte)(square >> 3); // Equivalent to square / 8
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSecondRank(this byte rankIndex)
    {
        return rankIndex == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSeventhRank(this byte rankIndex)
    {
        return rankIndex == 6;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWhiteEnPassantRankIndex(this byte rankIndex)
    {
        return rankIndex == 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBlackEnPassantRankIndex(this byte rankIndex)
    {
        return rankIndex == 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ShiftUp(this byte board)
    {
        return (byte)(board + 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ShiftDown(this byte board)
    {
        return (byte)(board - 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ShiftLeft(this byte board)
    {
        return (byte)(board - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ShiftRight(this byte board)
    {
        return (byte)(board + 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ShiftUpRight(this byte board)
    {
        return (byte)(board + 9);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ShiftUpLeft(this byte board)
    {
        return (byte)(board + 7);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ShiftDownRight(this byte board)
    {
        return (byte)(board - 7);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ShiftDownLeft(this byte board)
    {
        return (byte)(board - 9);
    }
}