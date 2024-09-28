using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;
using System.Runtime.InteropServices;

namespace Sapling.Engine;

[StructLayout(LayoutKind.Explicit, Size = 138)]
public unsafe struct BoardStateData
{
    [FieldOffset(0)] public fixed ulong Occupancy[15];
    [FieldOffset(120)] public ulong Hash;
    [FieldOffset(128)] public ushort TurnCount;
    [FieldOffset(130)] public byte HalfMoveClock;
    [FieldOffset(131)] public bool WhiteToMove;
    [FieldOffset(132)] public bool InCheck;
    [FieldOffset(133)] public CastleRights CastleRights;
    [FieldOffset(134)] public byte WhiteKingSquare;
    [FieldOffset(135)] public byte BlackKingSquare;
    [FieldOffset(136)] public byte EnPassantFile;
    [FieldOffset(137)] public byte PieceCount;
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
    public static void CloneTo(this ref AccumulatorState board, ref AccumulatorState copy)
    {
        fixed (AccumulatorState* sourcePtr = &board)
        fixed (AccumulatorState* destPtr = &copy)
        {
            // Copy the memory block from source to destination
            Buffer.MemoryCopy(sourcePtr, destPtr, sizeof(AccumulatorState), sizeof(AccumulatorState));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateTo(this ref AccumulatorState state, ref BoardStateData board)
    {
        state.Evaluation = null;
        state.WhiteNeedsRefresh = state.BlackNeedsRefresh = false;
        state.WhiteMirrored = board.WhiteKingSquare.IsMirroredSide();
        state.WhiteInputBucket = NnueWeights.BucketLayout[board.WhiteKingSquare];
        state.BlackMirrored = board.BlackKingSquare.IsMirroredSide();
        state.BlackInputBucket = NnueWeights.BucketLayout[board.BlackKingSquare ^ 0x38];

        state.WhiteAccumulatorUpToDate = state.BlackAccumulatorUpToDate = false;
        state.ChangeType = AccumulatorChangeType.None;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateToParent(this ref AccumulatorState state, ref AccumulatorState other, ref BoardStateData board)
    {
        state.Evaluation = null;
        state.WhiteMirrored = board.WhiteKingSquare.IsMirroredSide();
        state.WhiteInputBucket = NnueWeights.BucketLayout[board.WhiteKingSquare];
        state.BlackMirrored = board.BlackKingSquare.IsMirroredSide();
        state.BlackInputBucket = NnueWeights.BucketLayout[board.BlackKingSquare ^ 0x38];

        state.WhiteAccumulatorUpToDate = state.BlackAccumulatorUpToDate = false;
        state.ChangeType = AccumulatorChangeType.None;

        state.WhiteNeedsRefresh = other.WhiteMirrored != state.WhiteMirrored || other.WhiteInputBucket != state.WhiteInputBucket;
        state.BlackNeedsRefresh = other.BlackMirrored != state.BlackMirrored || other.BlackInputBucket != state.BlackInputBucket;
    }
}

public enum AccumulatorChangeType : byte
{
    None = 0,
    SubAdd = 1,
    SubSubAdd = 2,
    SubSubAddAdd = 3,
}

[StructLayout(LayoutKind.Explicit, Size = 45)]
public unsafe struct AccumulatorState
{
    [FieldOffset(0)] public fixed int WhiteAddFeatureUpdates[2];
    [FieldOffset(8)] public fixed int WhiteSubFeatureUpdates[2]; 
    [FieldOffset(16)] public fixed int BlackAddFeatureUpdates[2];
    [FieldOffset(24)] public fixed int BlackSubFeatureUpdates[2];
    [FieldOffset(32)] public AccumulatorChangeType ChangeType;
    [FieldOffset(33)] public bool BlackAccumulatorUpToDate;
    [FieldOffset(34)] public bool WhiteAccumulatorUpToDate;
    [FieldOffset(35)] public bool WhiteMirrored;
    [FieldOffset(36)] public bool BlackMirrored;
    [FieldOffset(37)] public byte WhiteInputBucket;
    [FieldOffset(38)] public byte BlackInputBucket;
    [FieldOffset(39)] public bool WhiteNeedsRefresh;
    [FieldOffset(40)] public bool BlackNeedsRefresh;
    [FieldOffset(41)] public int? Evaluation;
}

public unsafe struct BoardStateEntry
{
    public BoardStateData Data = default;
    public readonly VectorShort* BlackAccumulator;
    public readonly VectorShort* WhiteAccumulator;
    public AccumulatorState AccumulatorState = default;

    public BoardStateEntry()
    {
        WhiteAccumulator = AllocateAccumulator();
        BlackAccumulator = AllocateAccumulator();
    }
    public static VectorShort* AllocateAccumulator()
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)NnueEvaluator.L1ByteSize, alignment);
        NativeMemory.Clear(block, (nuint)NnueEvaluator.L1ByteSize);

        return (VectorShort*)block;
    }
    public void Dispose()
    {
        NativeMemory.AlignedFree(WhiteAccumulator);
        NativeMemory.AlignedFree(BlackAccumulator);
    }
}