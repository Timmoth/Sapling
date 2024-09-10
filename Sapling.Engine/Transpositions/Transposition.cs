using System.Runtime.InteropServices;

namespace Sapling.Engine.Transpositions;

[StructLayout(LayoutKind.Explicit, Pack = 2, Size = 18)]
public struct Transposition
{
    [FieldOffset(0)] public ulong FullHash;
    [FieldOffset(8)] public int Evaluation;
    [FieldOffset(12)] public uint Move;
    [FieldOffset(16)] public TranspositionTableFlag Flag;
    [FieldOffset(17)] public byte Depth;
}