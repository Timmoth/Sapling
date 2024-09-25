using Sapling.Engine.Evaluation;

namespace Sapling.Engine;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit, Size = 144)] // Explicit layout with size control
public unsafe struct BoardStateData
{
    // 8-byte fields (grouped together for optimal alignment)
    [FieldOffset(0)] public fixed ulong Occupancy[15];

    // Hash, which is 8 bytes
    [FieldOffset(120)] public ulong Hash;         // 8 bytes
    [FieldOffset(128)] public ushort TurnCount;     // 2 bytes
    [FieldOffset(130)] public byte HalfMoveClock;        // 1 bytes

    // Grouped bools (using 1 byte each)
    [FieldOffset(131)] public bool WhiteToMove;         // 1 byte
    [FieldOffset(132)] public bool InCheck;             // 1 byte
    [FieldOffset(133)] public bool WhiteMirrored;       // 1 byte
    [FieldOffset(134)] public bool BlackMirrored;       // 1 byte

    // 4-byte field (for proper alignment)

    // CastleRights (assuming this is a byte-sized enum)
    [FieldOffset(135)] public CastleRights CastleRights; // 1 byte

    // Smaller fields (grouped together for minimal padding)
    [FieldOffset(136)] public byte WhiteKingSquare; // 1 byte
    [FieldOffset(137)] public byte BlackKingSquare; // 1 byte
    [FieldOffset(138)] public byte EnPassantFile;   // 1 byte
    [FieldOffset(139)] public byte PieceCount;      // 1 byte
    [FieldOffset(140)] public byte WhiteInputBucket;      // 1 byte
    [FieldOffset(141)] public byte BlackInputBucket;      // 1 byte
    [FieldOffset(142)] public bool WhiteNeedsRefresh;      // 1 byte
    [FieldOffset(143)] public bool BlackNeedsRefresh;      // 1 byte
    public void CloneTo(ref BoardStateData copy)
    {
        fixed (BoardStateData* sourcePtr = &this)
        fixed (BoardStateData* destPtr = &copy)
        {
            // Copy the memory block from source to destination
            Buffer.MemoryCopy(sourcePtr, destPtr, sizeof(BoardStateData), sizeof(BoardStateData));
        }
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