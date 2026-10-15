using System.Runtime.InteropServices;

namespace Sapling.Engine.Transpositions;

// 16-byte stride: 4 entries per cache line, every entry naturally 16B-aligned
// (the table block is 64B-aligned), so the 64-bit Stamp is always an aligned
// atomic access. The payload (move/eval/depth/flag) lives entirely inside
// Stamp, so a reader sees either the whole old payload or the whole new one —
// no seqlock version counter is needed. The only cross-word race is Stamp vs
// FullHash; callers always validate snapshot.FullHash against the probe hash,
// which turns any mixed read into a harmless miss.
//
// Stamp layout (little-endian bit packing):
//   bits  0-31  Move
//   bits 32-47  Evaluation (signed i16; mate scores stay within +/-29128)
//   bits 48-55  Depth
//   bits 56-57  Flag
//   bits 58-63  Version (seqlock: even = stable, odd = writer claim)
//
// The payload (move/eval/depth/flag) lives entirely inside Stamp, so a reader
// that sees a stable even version has a consistent payload by construction.
// The Stamp/FullHash pair is bracketed by two Stamp reads: any generation mix
// shows up as a Stamp mismatch or a FullHash mismatch and becomes a miss.
[StructLayout(LayoutKind.Explicit, Size = 16)]
public unsafe struct Transposition
{
    [FieldOffset(0)] public ulong FullHash;
    [FieldOffset(8)] public ulong Stamp;

    public uint Move
    {
        readonly get => (uint)(Stamp & 0xFFFF_FFFFUL);
        set => Stamp = (Stamp & ~0xFFFF_FFFFUL) | value;
    }

    public int Evaluation
    {
        readonly get => (short)(Stamp >> 32);
        set => Stamp = (Stamp & 0xFFFF_0000_FFFF_FFFFUL) | ((ulong)(ushort)value << 32);
    }

    public byte Depth
    {
        readonly get => (byte)(Stamp >> 48);
        set => Stamp = (Stamp & 0x0000_FFFF_FFFF_FFFFUL) | ((ulong)value << 48);
    }

    public TranspositionTableFlag Flag
    {
        readonly get => (TranspositionTableFlag)(byte)((Stamp >> 56) & 3);
        set => Stamp = (Stamp & 0x00FF_FFFF_FFFF_FFFFUL) | ((ulong)(byte)value << 56);
    }

    public readonly ushort Version => (ushort)((Stamp >> 58) & 0x3F);

    public static ulong PackStamp(uint move, int evaluation, byte depth, TranspositionTableFlag flag, int version) =>
        move | ((ulong)(ushort)evaluation << 32) | ((ulong)depth << 48) | ((ulong)(byte)flag << 56) |
        ((ulong)(version & 0x3F) << 58);
}
