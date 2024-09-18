using Sapling.Engine.Evaluation;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace Sapling.Engine;
#if AVX512
using AvxIntrinsics = System.Runtime.Intrinsics.X86.Avx512BW;
using VectorType = System.Runtime.Intrinsics.Vector512;
using VectorInt = System.Runtime.Intrinsics.Vector512<int>;
using VectorShort = System.Runtime.Intrinsics.Vector512<short>;
#else
using AvxIntrinsics = Avx2;
using VectorType = Vector256;
using VectorInt = Vector256<int>;
using VectorShort = Vector256<short>;
#endif

using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit, Size = 145)] // Explicit layout with size control
public unsafe struct BoardStateData
{
    // 8-byte fields (grouped together for optimal alignment)
    [FieldOffset(0)] public ulong Occupancy;      // 8 bytes
    [FieldOffset(8)] public ulong WhitePieces;    // 8 bytes
    [FieldOffset(16)] public ulong BlackPieces;   // 8 bytes

    [FieldOffset(24)] public ulong WhitePawns;    // 8 bytes
    [FieldOffset(32)] public ulong WhiteKnights;  // 8 bytes
    [FieldOffset(40)] public ulong WhiteBishops;  // 8 bytes
    [FieldOffset(48)] public ulong WhiteRooks;    // 8 bytes
    [FieldOffset(56)] public ulong WhiteQueens;   // 8 bytes
    [FieldOffset(64)] public ulong WhiteKings;    // 8 bytes

    [FieldOffset(72)] public ulong BlackPawns;    // 8 bytes
    [FieldOffset(80)] public ulong BlackKnights;  // 8 bytes
    [FieldOffset(88)] public ulong BlackBishops;  // 8 bytes
    [FieldOffset(96)] public ulong BlackRooks;    // 8 bytes
    [FieldOffset(104)] public ulong BlackQueens;  // 8 bytes
    [FieldOffset(112)] public ulong BlackKings;   // 8 bytes

    // Hash, which is 8 bytes
    [FieldOffset(120)] public ulong Hash;         // 8 bytes

    // Smaller fields (grouped together for minimal padding)
    [FieldOffset(128)] public byte WhiteKingSquare; // 1 byte
    [FieldOffset(129)] public byte BlackKingSquare; // 1 byte
    [FieldOffset(130)] public byte EnPassantFile;   // 1 byte
    [FieldOffset(131)] public byte PieceCount;      // 1 byte

    [FieldOffset(132)] public ushort TurnCount;     // 2 bytes

    // Grouped bools (using 1 byte each)
    [FieldOffset(134)] public bool WhiteToMove;         // 1 byte
    [FieldOffset(135)] public bool InCheck;             // 1 byte
    [FieldOffset(136)] public bool ShouldWhiteMirrored; // 1 byte
    [FieldOffset(137)] public bool ShouldBlackMirrored; // 1 byte
    [FieldOffset(138)] public bool WhiteMirrored;       // 1 byte
    [FieldOffset(139)] public bool BlackMirrored;       // 1 byte

    // 4-byte field (for proper alignment)
    [FieldOffset(140)] public int HalfMoveClock;        // 4 bytes

    // CastleRights (assuming this is a byte-sized enum)
    [FieldOffset(144)] public CastleRights CastleRights; // 1 byte

public void CloneTo(ref BoardStateData copy)
    {
        var spanSource = MemoryMarshal.CreateSpan(ref this, 1);
        var spanDest = MemoryMarshal.CreateSpan(ref copy, 1);
        spanSource.CopyTo(spanDest);
    }
}

public sealed unsafe class BoardState
{
    public BoardStateData Data;
    public readonly ulong* Moves;
    public VectorShort* BlackAccumulator;
    public VectorShort* WhiteAccumulator;

    public BoardState()
    {
        WhiteAccumulator = AllocateAccumulator();
        BlackAccumulator = AllocateAccumulator();
        Moves = AllocateMoves();
    }

    public static ulong* AllocateMoves()
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)sizeof(ulong) * 800, alignment);
        NativeMemory.Clear(block, (nuint)sizeof(ulong) * 800);

        return (ulong*)block;
    }
    public static VectorShort* AllocateAccumulator()
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)NnueEvaluator.L1ByteSize, alignment);
        NativeMemory.Clear(block, (nuint)NnueEvaluator.L1ByteSize);

        return (VectorShort*)block;
    }

    ~BoardState()
    {
        NativeMemory.AlignedFree(Moves);
        NativeMemory.AlignedFree(WhiteAccumulator);
        NativeMemory.AlignedFree(BlackAccumulator);
    }
}