using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Search;

public static unsafe class HistoryHeuristicExtensions
{
    private const int MaxHistory = 8192;
    private const short BonusMax = 640;
    private const short BonusCoeff = 80;

    public static short* BonusTable;
    static HistoryHeuristicExtensions()
    {
        BonusTable = Allocate();
        for (var i = 0; i < Constants.MaxSearchDepth; i++)
        {
            BonusTable[i] = Math.Min(BonusMax, (short)(BonusCoeff * (i - 1)));
        }
    }

    public static short* Allocate()
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)sizeof(short) * Constants.MaxSearchDepth, alignment);
        NativeMemory.Clear(block, (nuint)sizeof(short) * Constants.MaxSearchDepth);

        return (short*)block;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void UpdateMovesHistory(int* history, uint* moves, int quietCount, uint m, int depth)
    {
        var bonus = BonusTable[depth];
        var absBonus = Math.Abs(bonus);

        // Directly update the history array
        var index = m.GetMovedPiece() * 64 + m.GetToSquare();
        history[index] += bonus - (history[index] * absBonus) / MaxHistory;

        var malus = (short)-bonus;

        // Process quiet moves
        for (var n = 0; n < quietCount; n++)
        {
            var quiet = moves[n];
            if (!quiet.IsQuiet())
            {
                continue;
            }

            var quietIndex = quiet.GetMovedPiece() * 64 + quiet.GetToSquare();
            history[quietIndex] += malus - (history[quietIndex] * absBonus) / MaxHistory;
        }
    }
}