using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;
using System.Runtime.InteropServices;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine;

[StructLayout(LayoutKind.Explicit, Size = 168)]  // Adjusted size to fit more efficiently
public unsafe struct BoardStateData
{
    public const uint BoardStateSize = 168;

    // 15 * 8 = 120 bytes (occupancy array)
    [FieldOffset(0)] public fixed ulong Occupancy[15];  // 15 ulong values, no padding needed.

    // 8 bytes, aligned at 120
    [FieldOffset(120)] public ulong Hash;  // 64-bit aligned naturally.

    // Keep all four hashes aligned
    [FieldOffset(128)] public ulong PawnHash;
    [FieldOffset(136)] public ulong WhiteMaterialHash;
    [FieldOffset(144)] public ulong BlackMaterialHash;

    [FieldOffset(152)] public ushort TurnCount;
    [FieldOffset(154)] public byte HalfMoveClock;
    [FieldOffset(155)] public bool WhiteToMove;
    [FieldOffset(156)] public bool InCheck;
    [FieldOffset(157)] public CastleRights CastleRights;
    [FieldOffset(158)] public byte WhiteKingSquare;
    [FieldOffset(159)] public byte BlackKingSquare;
    [FieldOffset(160)] public byte EnPassantFile;
    [FieldOffset(161)] public byte PieceCount;

    // Group remaining small fields together (total 4 bytes):
    [FieldOffset(162)] public byte WhiteKingSideTargetSquare;  // 1 byte
    [FieldOffset(163)] public byte WhiteQueenSideTargetSquare;  // 1 byte
    [FieldOffset(164)] public byte BlackKingSideTargetSquare;  // 1 byte
    [FieldOffset(165)] public byte BlackQueenSideTargetSquare;  // 1 byte
    [FieldOffset(166)] public bool Is960;  // 1 byte

    // Add padding if necessary to make the total size a multiple of 8 bytes.
    [FieldOffset(167)] private fixed byte _padding[1];  // Padding to align total size to 168 bytes.
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
