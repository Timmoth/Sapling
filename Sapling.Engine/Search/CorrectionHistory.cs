using System.Runtime.CompilerServices;

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
    public const int CorrectionScale = 1024;
    public const int CorrectionGrain = 256;
    public const short CorrectionMax = CorrectionGrain * 64;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void UpdateCorrectionHistory(int index, int diff, int depth)
    {
        var scaledWeight = Math.Min((depth * depth) + 1, 128);

        var pawnCh = PawnCorrHist + index;
        var pawnBonus = (*(pawnCh) * (CorrectionScale - scaledWeight) + (diff * CorrectionGrain * scaledWeight)) / CorrectionScale;
        *pawnCh = Math.Clamp(pawnBonus, -CorrectionMax, CorrectionMax);
    }

    const int MinMateScore = Constants.ImmediateMateScore - Constants.MaxSearchDepth;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe int AdjustEval(int index, int rawEval)
    {
        return (rawEval + *(PawnCorrHist + index) / CorrectionGrain);
    }
}