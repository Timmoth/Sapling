using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sapling.Engine.Search;

public partial class Searcher
{
    public const int TableSize = 16384;
    public const int TableElementsSize = TableSize * 2;
    public const int CorrectionTableMask = TableSize - 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CorrectionIndex(ulong pawnHash, bool whiteToMove)
    {
        return (whiteToMove ? 0 : 1 * TableSize) + (int)(pawnHash & CorrectionTableMask);
    }
    public const int CorrectionScale = 256;
    public const int CorrectionGrain = 256;
    public const short CorrectionMax = CorrectionGrain * 32;

    // Cleared on ucinewgame, not between searches.
    public unsafe void ClearCorrectionHistory()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
        NativeMemory.Clear(PawnCorrHist, (nuint)TableElementsSize * sizeof(int));
        NativeMemory.Clear(WhiteMaterialCorrHist, (nuint)TableElementsSize * sizeof(int));
        NativeMemory.Clear(BlackMaterialCorrHist, (nuint)TableElementsSize * sizeof(int));
        NativeMemory.Clear(PreviousMoveCorrHist, (nuint)CountersLength * sizeof(int));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Update(ref int entry, int newWeight, int scaledDiff)
    {
        int update = entry * (CorrectionScale - newWeight) + scaledDiff * newWeight;
        entry = Math.Clamp(update / CorrectionScale, -CorrectionMax, CorrectionMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void UpdateCorrectionHistory(int pawnChIndex, int whiteMaterialChIndex, int blackMaterialChIndex,
        uint previousMoveIndex, int diff, int depth)
    {
        int scaledDiff = diff * CorrectionGrain;
        int newWeight = Math.Min(16, 1 + depth);

        Update(ref *(PawnCorrHist + pawnChIndex), newWeight, scaledDiff);
        Update(ref *(WhiteMaterialCorrHist + whiteMaterialChIndex), newWeight, scaledDiff);
        Update(ref *(BlackMaterialCorrHist + blackMaterialChIndex), newWeight, scaledDiff);

        // Keep the continuation-keyed correction deliberately half as influential as
        // each existing table so it adds context without dominating their combined signal.
        if (previousMoveIndex != 0)
            Update(ref *(PreviousMoveCorrHist + previousMoveIndex), Math.Max(1, newWeight / 2), scaledDiff);

    }

    const int MinMateScore = Constants.ImmediateMateScore - Constants.MaxSearchDepth;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe int AdjustEval(int pawnChIndex, int whiteMaterialChIndex, int blackMaterialChIndex,
        uint previousMoveIndex, int rawEval)
    {
        var pch = *(PawnCorrHist + pawnChIndex);
        var mchW = *(WhiteMaterialCorrHist + whiteMaterialChIndex);
        var mchB = *(BlackMaterialCorrHist + blackMaterialChIndex);

        var previousMoveCorrection = previousMoveIndex == 0 ? 0 : *(PreviousMoveCorrHist + previousMoveIndex) / 2;
        return rawEval + (int)((pch + mchW + mchB + previousMoveCorrection) / CorrectionGrain);

    }
}
