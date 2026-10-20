using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    internal const int MoveContextCount = 13 * 64;
    internal const int ContinuationHistoryLength = MoveContextCount * MoveContextCount;
    internal const int PawnMoveHistoryLength = 512 * MoveContextCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ContinuationIndex(uint previous, uint move) =>
        (int)previous.GetCounterMoveIndex() * MoveContextCount + (int)move.GetCounterMoveIndex();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int PawnMoveIndex(ulong pawnHash, uint move) =>
        (int)(pawnHash & 511) * MoveContextCount + (int)move.GetCounterMoveIndex();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int QuietContextScore(uint previous, ulong pawnHash, uint move) =>
        PawnMoveHistory[PawnMoveIndex(pawnHash, move)] / 4 +
        (previous == 0 ? 0 : ContinuationHistory[ContinuationIndex(previous, move)] / 4);

    internal void UpdateQuietContexts(uint previous, ulong pawnHash, uint winner,
        uint* searched, int count, int depth)
    {
        var bonus = Math.Min(512, Math.Max(1, depth) * 32);
        UpdateQuietContext(previous, pawnHash, winner, bonus);
        for (var i = 0; i < count; i++)
            if (searched[i] != winner && searched[i].IsQuiet())
                UpdateQuietContext(previous, pawnHash, searched[i], -bonus);
    }

    private void UpdateQuietContext(uint previous, ulong pawnHash, uint move, int bonus)
    {
        ApplyContextBonus(ref PawnMoveHistory[PawnMoveIndex(pawnHash, move)], bonus);
        if (previous != 0)
            ApplyContextBonus(ref ContinuationHistory[ContinuationIndex(previous, move)], bonus);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyContextBonus(ref short entry, int bonus)
    {
        const int limit = 8192;
        entry = (short)Math.Clamp(entry + bonus - entry * Math.Abs(bonus) / limit, -limit, limit);
    }
}
