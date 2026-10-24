using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    private const int MoveContextCount = 12 * 64;
    internal const int ContinuationHistoryLength = MoveContextCount * MoveContextCount;
    private const int ContinuationLimit = 8192;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ContinuationIndex(uint previous, uint move) =>
        ((int)previous.GetCounterMoveIndex() - 64) * MoveContextCount +
        ((int)move.GetCounterMoveIndex() - 64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int ContinuationScore(uint previous, uint earlier, uint move)
    {
        if (previous == 0) return 0;
        return ContinuationHistory[ContinuationIndex(previous, move)] / 2 +
            (earlier == 0 ? 0 : ContinuationHistory[ContinuationIndex(earlier, move)] / 4);
    }

    internal void UpdateContinuations(uint previous, uint earlier, uint winner, uint* searched, int count, int depth)
    {
        if (previous == 0) return;
        var bonus = Math.Min(1024, 96 * Math.Max(1, depth) - 32);
        UpdateContinuation(previous, earlier, winner, bonus);
        for (var i = 0; i < count; i++)
            if (searched[i] != winner && searched[i].IsQuiet())
                UpdateContinuation(previous, earlier, searched[i], -bonus);
    }

    private void UpdateContinuation(uint previous, uint earlier, uint move, int bonus)
    {
        ApplyContinuationBonus(ref ContinuationHistory[ContinuationIndex(previous, move)], bonus);
        if (earlier != 0)
            ApplyContinuationBonus(ref ContinuationHistory[ContinuationIndex(earlier, move)], bonus / 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyContinuationBonus(ref short entry, int bonus) =>
        entry = (short)Math.Clamp(entry + bonus - entry * Math.Abs(bonus) / ContinuationLimit,
            -ContinuationLimit, ContinuationLimit);
}
