using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    internal const int CaptureHistoryLength = 13 * 64 * 13;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CaptureHistoryIndex(uint move) =>
        ((move.GetMovedPiece() * 64) + move.GetToSquare()) * 13 + move.GetCapturedPiece();

    internal void UpdateCaptureHistories(uint winner, uint* searched, int count, int depth)
    {
        var bonus = Math.Min(512, 32 * Math.Max(depth, 1));
        if (winner.IsCapture())
            ApplyCaptureBonus(ref CaptureHistory[CaptureHistoryIndex(winner)], bonus);
        for (var i = 0; i < count; i++)
            if (searched[i] != winner && searched[i].IsCapture())
                ApplyCaptureBonus(ref CaptureHistory[CaptureHistoryIndex(searched[i])], -bonus);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyCaptureBonus(ref short entry, int bonus)
    {
        const int limit = 8192;
        entry = (short)Math.Clamp(entry + bonus - entry * Math.Abs(bonus) / limit, -limit, limit);
    }
}
