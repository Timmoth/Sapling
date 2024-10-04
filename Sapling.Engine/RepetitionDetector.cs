using System.Runtime.CompilerServices;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine;

public static unsafe class RepetitionDetector
{
    private static readonly uint* Moves;
    private static readonly ulong* Keys;

    private const int TableSize = 8192;


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash1(ulong key) => (int)(key & 0x1FFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash2(ulong key) => (int)((key >> 16) & 0x1FFF);

    static RepetitionDetector()
    {
        Moves = MemoryHelpers.Allocate<uint>(TableSize);
        Keys = MemoryHelpers.Allocate<ulong>(TableSize);
        new Span<uint>(Moves, TableSize).Fill(0);

        for (var piece = 3; piece <= 12; piece++)
        {
            for (var fromSquare = 0; fromSquare < 64; fromSquare++)
            {
                var fromHash = Zobrist.PiecesArray[piece * 64 + fromSquare];
                var attackMask = AttackMask(fromSquare, piece);

                for (var toSquare = fromSquare + 1; toSquare < 64; toSquare++)
                {
                    var destSquare = (1UL << toSquare);

                    if ((attackMask & destSquare) == 0)
                    {
                        continue;
                    }

                    var toHash = Zobrist.PiecesArray[piece * 64 + toSquare];
                    var move = MoveExtensions.EncodeNormalMove(piece, fromSquare, toSquare);
                    var key = fromHash ^ toHash ^ Zobrist.SideToMove;

                    var i = Hash1(key);
                    while (true)
                    {
                        (Keys[i], key) = (key, Keys[i]);
                        (Moves[i], move) = (move, Moves[i]);

                        if (i == 0)
                            break;

                        i = i == Hash1(key) ? Hash2(key) : Hash1(key);
                    }
                }
            }
        }

        ulong AttackMask(int idx, int piece)
        {
            return piece switch
            {
                Constants.WhiteKnight => AttackTables.KnightAttackTable[idx],
                Constants.BlackKnight => AttackTables.KnightAttackTable[idx],
                Constants.WhiteBishop => AttackTables.BishopAttackMasksAll[idx],
                Constants.BlackBishop => AttackTables.BishopAttackMasksAll[idx],
                Constants.WhiteRook => AttackTables.RookAttackMasksAll[idx],
                Constants.BlackRook => AttackTables.RookAttackMasksAll[idx],
                Constants.WhiteQueen => AttackTables.BishopAttackMasksAll[idx] | AttackTables.RookAttackMasksAll[idx],
                Constants.BlackQueen => AttackTables.BishopAttackMasksAll[idx] | AttackTables.RookAttackMasksAll[idx],
                Constants.WhiteKing => AttackTables.KingAttackTable[idx],
                Constants.BlackKing => AttackTables.KingAttackTable[idx],
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasRepetition(this ref BoardStateData pos, ulong* hashHistory, int depthFromRoot)
    {
        if (pos.HalfMoveClock < 3)
            return false;

        var lastMoveIndex = pos.TurnCount - 1;
        var occupancy = pos.Occupancy[Constants.Occupancy];
        int slot;
        for (var i = 3; i <= pos.HalfMoveClock && i < lastMoveIndex; i += 2)
        {
            var diff = pos.Hash ^ *(hashHistory + lastMoveIndex - i);

            if (diff != Keys[(slot = Hash1(diff))] &&
                diff != Keys[(slot = Hash2(diff))])
                continue;

            var m = Moves[slot];
            int moveFrom = (int)m.GetFromSquare();
            int moveTo = (int)m.GetToSquare();

            if ((occupancy & *(AttackTables.LineBitBoards+((moveFrom << 6) + moveTo))) != 0)
            {
                continue;
            }

            if (depthFromRoot > i)
                return true;

            var isWhite = false;
            if ((occupancy & (1ul << moveFrom)) != 0)
            {
                isWhite = (pos.Occupancy[Constants.WhitePieces] & (1ul << moveFrom)) != 0;
            }
            else
            {
                isWhite = (pos.Occupancy[Constants.WhitePieces] & (1ul << moveTo)) != 0;
            }

            return isWhite == pos.WhiteToMove;
        }

        return false;
    }
}