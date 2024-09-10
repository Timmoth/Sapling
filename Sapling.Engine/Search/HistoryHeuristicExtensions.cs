using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Search;

public static class HistoryHeuristicExtensions
{
    private const int MaxHistory = 8192;
    private const short BonusMax = 640;
    private const short BonusCoeff = 80;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateMovesHistory(this Span<int> history, Span<uint> moves, int quietCount, uint m, int depth)
    {
        var bonus = Math.Min(BonusMax, (short)(BonusCoeff * (depth - 1)));
        AddMoveHistoryBonus(ref history[m.GetMovedPiece() * 64 + m.GetToSquare()], bonus);

        var malus = (short)-bonus;
        for (var n = 0; n < quietCount; n++)
        {
            var quiet = moves[n];
            if (quiet.IsQuiet())
            {
                AddMoveHistoryBonus(ref history[quiet.GetMovedPiece() * 64 + quiet.GetToSquare()], malus);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddMoveHistoryBonus(ref int hist, short bonus)
    {
        hist += bonus - hist * Math.Abs(bonus) / MaxHistory;
    }
}