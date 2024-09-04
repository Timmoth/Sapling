using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace iBlunder.Engine;

public static class BitboardHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte PopLSB(this ref ulong b)
    {
        var i = (byte)Bmi1.X64.TrailingZeroCount(b);
        //b &= b - 1;
        b = Bmi1.X64.ResetLowestSetBit(b);

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PeekLSB(this ulong bitBoard)
    {
        return Bmi1.X64.TrailingZeroCount(bitBoard);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte PopCount(ulong bitBoard)
    {
        return (byte)Popcnt.X64.PopCount(bitBoard);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong RankFileToBitboard(int rank, int file)
    {
        return 1UL << (rank * 8 + file);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SquareToBitboard(this int square)
    {
        return 1UL << square;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftUp(this ulong board)
    {
        return board << 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftDown(this ulong board)
    {
        return board >> 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftLeft(this ulong board)
    {
        return (board >> 1) & Constants.NotHFile;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftRight(this ulong board)
    {
        return (board << 1) & Constants.NotAFile;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftUpRight(this ulong board)
    {
        return board.ShiftUp().ShiftRight();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftUpLeft(this ulong board)
    {
        return board.ShiftUp().ShiftLeft();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftDownRight(this ulong board)
    {
        return board.ShiftDown().ShiftRight();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftDownLeft(this ulong board)
    {
        return board.ShiftDown().ShiftLeft();
    }
}