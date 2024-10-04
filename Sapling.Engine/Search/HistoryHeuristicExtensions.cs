using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Search;

public static unsafe class HistoryHeuristicExtensions
{
    public static short* BonusTable;
    static HistoryHeuristicExtensions()
    {
        BonusTable = MemoryHelpers.Allocate<short>(Constants.MaxSearchDepth);
        for (var i = 0; i < Constants.MaxSearchDepth; i++)
        {
            BonusTable[i] = Math.Min((short)SpsaOptions.HistoryHeuristicBonusMax, (short)(SpsaOptions.HistoryHeuristicBonusCoeff * (i - 1)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateMovesHistory(int* history, uint* moves, int quietCount, uint m, int depth)
    {
        var bonus = *(BonusTable + depth);

        // Directly update the history array
        var index = m.GetCounterMoveIndex();

        *(history + index) += bonus - (*(history + index) * bonus) / SpsaOptions.HistoryHeuristicMaxHistory;

        var malus = (short)-bonus;

        // Process quiet moves
        for (var n = 0; n < quietCount; n++)
        {
            var quiet = *(moves + n);
            if (!quiet.IsQuiet() || quiet == default)
            {
                continue;
            }

            var quietIndex = quiet.GetCounterMoveIndex();
            *(history + quietIndex) += malus - (*(history + quietIndex) * bonus) / SpsaOptions.HistoryHeuristicMaxHistory;
        }
    }
}