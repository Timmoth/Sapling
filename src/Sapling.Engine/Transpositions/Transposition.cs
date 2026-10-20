using System.Runtime.InteropServices;

namespace Sapling.Engine.Transpositions;

// Four 16-byte entries occupy one 64-byte cluster. Moves use only 24 bits in
// Sapling's encoding, leaving room for an independent generation byte without
// reducing the six-bit sequence used by coherent shared reads.
// Stamp: move[0..23], eval[24..39], depth[40..47], bound[48..49],
// sequence[50..55], replacement generation[56..63].
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct Transposition
{
    internal const int VersionShift = 50;
    internal const ulong MoveMask = 0x00FF_FFFFUL;
    [FieldOffset(0)] public ulong FullHash;
    [FieldOffset(8)] public ulong Stamp;

    public uint Move
    {
        readonly get => (uint)(Stamp & MoveMask);
        set
        {
            if (value > MoveMask) throw new ArgumentOutOfRangeException(nameof(value));
            Stamp = (Stamp & ~MoveMask) | value;
        }
    }
    public int Evaluation
    {
        readonly get => (short)(Stamp >> 24);
        set => Stamp = (Stamp & ~(0xFFFFUL << 24)) | ((ulong)(ushort)value << 24);
    }
    public byte Depth
    {
        readonly get => (byte)(Stamp >> 40);
        set => Stamp = (Stamp & ~(0xFFUL << 40)) | ((ulong)value << 40);
    }
    public TranspositionTableFlag Flag
    {
        readonly get => (TranspositionTableFlag)((Stamp >> 48) & 3);
        set => Stamp = (Stamp & ~(3UL << 48)) | ((ulong)(byte)value << 48);
    }
    public readonly ushort Version => (ushort)((Stamp >> VersionShift) & 0x3F);
    public readonly byte Generation => (byte)(Stamp >> 56);

    public static ulong PackStamp(uint move, int evaluation, byte depth,
        TranspositionTableFlag flag, int version, byte generation = 0)
    {
        if (move > MoveMask) throw new ArgumentOutOfRangeException(nameof(move));
        return move | ((ulong)(ushort)evaluation << 24) | ((ulong)depth << 40) |
            ((ulong)(byte)flag << 48) | ((ulong)(version & 0x3F) << VersionShift) |
            ((ulong)generation << 56);
    }
}
