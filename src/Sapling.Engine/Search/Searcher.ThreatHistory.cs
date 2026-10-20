using System.Numerics;
using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Search;

public unsafe partial class Searcher
{
    // [side][source threatened][destination threatened][source][destination].
    internal const int ThreatHistoryLength = 2 * 2 * 2 * 64 * 64;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ThreatIndex(bool whiteToMove, ulong attacks, uint move)
    {
        int from = move.GetFromSquare(), to = move.GetToSquare();
        int context = ((whiteToMove ? 0 : 1) * 2 + (int)((attacks >> from) & 1)) * 2 +
            (int)((attacks >> to) & 1);
        return (context * 64 + from) * 64 + to;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int ThreatScore(bool whiteToMove, ulong attacks, uint move) =>
        ThreatHistory[ThreatIndex(whiteToMove, attacks, move)] / 2;

    internal void UpdateThreatHistories(bool whiteToMove, ulong attacks, uint winner, uint* searched, int count, int depth)
    {
        int bonus = Math.Min(1024, 96 * Math.Max(1, depth) - 32);
        ApplyThreatBonus(ref ThreatHistory[ThreatIndex(whiteToMove, attacks, winner)], bonus);
        for (var i = 0; i < count; i++)
            if (searched[i] != winner && searched[i].IsQuiet())
                ApplyThreatBonus(ref ThreatHistory[ThreatIndex(whiteToMove, attacks, searched[i])], -bonus);
    }

    private static void ApplyThreatBonus(ref short entry, int bonus) =>
        entry = (short)Math.Clamp(entry + bonus - entry * Math.Abs(bonus) / 8192, -8192, 8192);

    // Geometric enemy attacks in the parent position: include pinned pieces and defended
    // blockers, stop sliders at their first blocker. Compute once per main-search node.
    internal static ulong EnemyAttacks(BoardStateData* board)
    {
        bool white = !board->WhiteToMove;
        ulong occupied = board->Occupancy[Constants.Occupancy];
        ulong pawns = board->Occupancy[white ? Constants.WhitePawn : Constants.BlackPawn];
        const ulong notA = 0xfefefefefefefefeUL, notH = 0x7f7f7f7f7f7f7f7fUL;
        ulong attacks = white ? ((pawns & notA) << 7) | ((pawns & notH) << 9)
                              : ((pawns & notH) >> 7) | ((pawns & notA) >> 9);
        ulong pieces = board->Occupancy[white ? Constants.WhiteKnight : Constants.BlackKnight];
        while (pieces != 0)
        {
            int square = BitOperations.TrailingZeroCount(pieces);
            pieces &= pieces - 1;
            attacks |= AttackTables.KnightAttackTable[square];
        }
        pieces = board->Occupancy[white ? Constants.WhiteBishop : Constants.BlackBishop] |
            board->Occupancy[white ? Constants.WhiteQueen : Constants.BlackQueen];
        while (pieces != 0)
        {
            int square = BitOperations.TrailingZeroCount(pieces);
            pieces &= pieces - 1;
            attacks |= AttackTables.BishopAttacks(occupied, square);
        }
        pieces = board->Occupancy[white ? Constants.WhiteRook : Constants.BlackRook] |
            board->Occupancy[white ? Constants.WhiteQueen : Constants.BlackQueen];
        while (pieces != 0)
        {
            int square = BitOperations.TrailingZeroCount(pieces);
            pieces &= pieces - 1;
            attacks |= AttackTables.RookAttacks(occupied, square);
        }
        return attacks | AttackTables.KingAttackTable[white ? board->WhiteKingSquare : board->BlackKingSquare];
    }
}
