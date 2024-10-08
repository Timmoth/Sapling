using System.Runtime.InteropServices;

namespace Sapling.Engine.Transpositions;

[StructLayout(LayoutKind.Explicit, Size = 20)]  // Size aligned to 20 bytes
public unsafe struct Transposition
{
    // 8 bytes for ulong, aligned at offset 0
    [FieldOffset(0)] public ulong FullHash;

    // 4 bytes for int, aligned at offset 8
    [FieldOffset(8)] public int Evaluation;

    // 4 bytes for uint, aligned at offset 12
    [FieldOffset(12)] public uint Move;

    // 1 byte for TranspositionTableFlag, no alignment needed
    [FieldOffset(16)] public TranspositionTableFlag Flag;

    // 1 byte for Depth, packed right after Flag
    [FieldOffset(17)] public byte Depth;

    // 2 bytes of padding to align the size to 20 bytes
    [FieldOffset(18)] private fixed byte _padding[2];  // Padding for alignment
}