using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Evaluation;

public static class StaticExchangeEvaluator
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int StaticExchangeEvaluation(this BoardState board, Span<ulong> occupancyBitBoards,
        Span<short> captures, uint move)
    {
        captures[0] = PieceValues.PieceValue[move.GetCapturedPiece()];
        var lastPieceValue = PieceValues.PieceValue[move.GetMovedPiece()];

        if (captures[0] - lastPieceValue > 0)
        {
            // Return if we give up our piece but the exchange is still positive.
            return captures[0] - lastPieceValue;
        }

        var targetSquare = move.GetToSquare();
        // all pieces except the two involved in the initial move
        var occupancy = board.Occupancy & ~((1UL << move.GetFromSquare()) | (1UL << targetSquare));

        // Bit boards by piece type
        var pawns = occupancyBitBoards[2];
        var knights = occupancyBitBoards[3];
        var bishops = occupancyBitBoards[4];
        var rooks = occupancyBitBoards[5];
        var queens = occupancyBitBoards[6];
        var kings = occupancyBitBoards[7];

        // Calculate all squares that can attack the target square
        var attackers = ((AttackTables.PextBishopAttacks(occupancy, targetSquare) & (queens | bishops)) |
                         (AttackTables.PextRookAttacks(occupancy, targetSquare) & (queens | rooks)) |
                         (AttackTables.KnightAttackTable[targetSquare] & knights) |
                         (AttackTables.WhitePawnAttackTable[targetSquare] & board.BlackPawns) |
                         (AttackTables.BlackPawnAttackTable[targetSquare] & board.WhitePawns) |
                         (AttackTables.KingAttackTable[targetSquare] & kings)) & occupancy;

        // Starts off as the opponents turn
        var turn = board.WhiteToMove ? 1 : 0;
        var cIndex = 1;
        do
        {
            var remainingAttackers = attackers & occupancyBitBoards[turn] & occupancy;

            if (remainingAttackers == 0)
            {
                // No attacks left on the target square
                break;
            }

            captures[cIndex] = (short)(-captures[cIndex - 1] + lastPieceValue);

            // Remove the least valuable attacker
            ulong attacker;
            if ((attacker = pawns & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.PawnValue;
            }
            else if ((attacker = knights & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.KnightValue;
            }
            else if ((attacker = bishops & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.BishopValue;
            }
            else if ((attacker = rooks & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.RookValue;
            }
            else if ((attacker = queens & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.QueenValue;
            }
            else if ((attacker = kings & remainingAttackers) != 0)
            {
                lastPieceValue = Constants.KingValue;
            }

            // Attacker is removed from occupancy
            occupancy &= ~(1UL << (byte)Bmi1.X64.TrailingZeroCount(attacker));

            // Update attackers with any discovered attacks
            attackers |= ((AttackTables.PextBishopAttacks(occupancy, targetSquare) & (bishops | queens)) |
                          (AttackTables.PextRookAttacks(occupancy, targetSquare) & (rooks | queens))) & occupancy;

            // Flip turn
            turn ^= 1;
        } while (captures[cIndex++] - lastPieceValue <= 0);

        for (var n = cIndex - 1; n > 0; n--)
        {
            captures[n - 1] = (short)-Math.Max(-captures[n - 1], captures[n]);
        }

        return captures[0];
    }
}