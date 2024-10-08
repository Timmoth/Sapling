using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;
using System.Runtime.InteropServices;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine;

[StructLayout(LayoutKind.Explicit, Size = 164)]  // Updated size to fit alignment
public unsafe struct BoardStateData
{
    public const uint BoardStateSize = 164;

    // 15 * 8 = 120 bytes (no padding needed)
    [FieldOffset(0)] public fixed ulong Occupancy[15];  // 15 ulong values

    // 8 bytes, aligned at 120
    [FieldOffset(120)] public ulong Hash;  // 64-bit, aligned at 120

    // 2 bytes, aligned at 128 (needs no padding since Hash is 64-bit)
    [FieldOffset(128)] public ushort TurnCount;  // 16-bit

    // 1 byte, follows TurnCount (16-bit aligned, so no padding needed)
    [FieldOffset(130)] public byte HalfMoveClock;  // 8-bit

    // 1 byte, no special alignment
    [FieldOffset(131)] public bool WhiteToMove;  // 8-bit

    // 1 byte, no special alignment
    [FieldOffset(132)] public bool InCheck;  // 8-bit

    // 1 byte, no special alignment, still on byte boundary
    [FieldOffset(133)] public CastleRights CastleRights;  // Enum type, typically 1 byte

    // Now grouping the remaining 1-byte fields together
    [FieldOffset(134)] public byte WhiteKingSquare;  // 1 byte
    [FieldOffset(135)] public byte BlackKingSquare;  // 1 byte
    [FieldOffset(136)] public byte EnPassantFile;  // 1 byte
    [FieldOffset(137)] public byte PieceCount;  // 1 byte

    // Add padding for alignment (2 bytes of padding at the end to make the size a multiple of 8)
    [FieldOffset(138)] private fixed byte _padding[2];  // Padding to align total size to 8-byte boundary
    [FieldOffset(140)] public ulong PawnHash;
    [FieldOffset(148)] public ulong WhiteMaterialHash;
    [FieldOffset(156)] public ulong BlackMaterialHash;

}

public static unsafe class AccumulatorStateExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CloneTo(this ref BoardStateData board, ref BoardStateData copy)
    {
        fixed (BoardStateData* sourcePtr = &board)
        fixed (BoardStateData* destPtr = &copy)
        {
            // Copy the memory block from source to destination
            Buffer.MemoryCopy(sourcePtr, destPtr, sizeof(BoardStateData), sizeof(BoardStateData));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateTo(this ref AccumulatorState state, BoardStateData* board)
    {
        state.Evaluation = TranspositionTableExtensions.NoHashEntry;
        state.WhiteNeedsRefresh = state.BlackNeedsRefresh = false;
        state.WhiteMirrored = board->WhiteKingSquare.IsMirroredSide();
        state.WhiteInputBucket = *(NnueWeights.BucketLayout + board->WhiteKingSquare);
        state.BlackMirrored = board->BlackKingSquare.IsMirroredSide();
        state.BlackInputBucket = *(NnueWeights.BucketLayout + (board->BlackKingSquare ^ 0x38));

        state.WhiteAccumulatorUpToDate = state.BlackAccumulatorUpToDate = false;
        state.ChangeType = AccumulatorChangeType.None;
        state.Move = default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateToParent(this ref AccumulatorState state, AccumulatorState* other, BoardStateData* board)
    {
        state.Evaluation = TranspositionTableExtensions.NoHashEntry;
        state.WhiteMirrored = board->WhiteKingSquare.IsMirroredSide();
        state.WhiteInputBucket = *(NnueWeights.BucketLayout + board->WhiteKingSquare);
        state.BlackMirrored = board->BlackKingSquare.IsMirroredSide();
        state.BlackInputBucket = *(NnueWeights.BucketLayout + (board->BlackKingSquare ^ 0x38));

        state.WhiteAccumulatorUpToDate = state.BlackAccumulatorUpToDate = false;
        state.ChangeType = AccumulatorChangeType.None;

        state.WhiteNeedsRefresh = other->WhiteMirrored != state.WhiteMirrored || other->WhiteInputBucket != state.WhiteInputBucket;
        state.BlackNeedsRefresh = other->BlackMirrored != state.BlackMirrored || other->BlackInputBucket != state.BlackInputBucket;
        state.Move = default;
    }
}

public enum AccumulatorChangeType : byte
{
    None = 0,
    SubAdd = 1,
    SubSubAdd = 2,
    SubSubAddAdd = 3,
}

[StructLayout(LayoutKind.Explicit, Size = 56)]  // Corrected size based on alignment needs
public unsafe struct AccumulatorState
{
    // 32-bit integers (4 bytes each)
    [FieldOffset(0)] public int WhiteAddFeatureUpdatesA;
    [FieldOffset(4)] public int WhiteAddFeatureUpdatesB;
    [FieldOffset(8)] public int WhiteSubFeatureUpdatesA;
    [FieldOffset(12)] public int WhiteSubFeatureUpdatesB;
    [FieldOffset(16)] public int BlackAddFeatureUpdatesA;
    [FieldOffset(20)] public int BlackAddFeatureUpdatesB;
    [FieldOffset(24)] public int BlackSubFeatureUpdatesA;
    [FieldOffset(28)] public int BlackSubFeatureUpdatesB;

    // 1-byte fields start after 32 bytes
    [FieldOffset(32)] public AccumulatorChangeType ChangeType;  // Enum type (1 byte)
    [FieldOffset(33)] public bool BlackAccumulatorUpToDate;     // 1 byte
    [FieldOffset(34)] public bool WhiteAccumulatorUpToDate;     // 1 byte
    [FieldOffset(35)] public bool WhiteMirrored;                // 1 byte
    [FieldOffset(36)] public bool BlackMirrored;                // 1 byte
    [FieldOffset(37)] public byte WhiteInputBucket;             // 1 byte
    [FieldOffset(38)] public byte BlackInputBucket;             // 1 byte
    [FieldOffset(39)] public bool WhiteNeedsRefresh;            // 1 byte
    [FieldOffset(40)] public bool BlackNeedsRefresh;            // 1 byte

    // Padding to align the next field (int) to a 4-byte boundary
    [FieldOffset(41)] private fixed byte _padding1[3];          // 3 bytes of padding

    // 32-bit fields aligned properly (starting at 44 bytes)
    [FieldOffset(44)] public int Evaluation;  // Changed nullable int to regular int for simplicity

    // 32-bit unsigned int (4 bytes)
    [FieldOffset(48)] public uint Move;       // 32-bit aligned correctly at 48 bytes
}
