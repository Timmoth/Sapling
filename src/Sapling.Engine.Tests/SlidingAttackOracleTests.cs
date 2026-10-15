using FluentAssertions;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

// independent ray-walk oracle for sliding attacks. Comparing PEXT only with
// magic tables derived from the same generator can reproduce a shared defect, so both
// production representations are compared against a simple ray walk over every relevant
// occupancy subset of every square.
//
// The dispatched attack API uses PEXT when available and magic otherwise.
// Both paths are compared with the independent oracle on supported hardware.
public unsafe class SlidingAttackOracleTests
{
    [Fact]
    public void PextAndMagicRookAttacks_MatchRayWalk_OverAllRelevantOccupancies()
    {
        for (var square = 0; square < 64; square++)
        {
            var mask = AttackTables.RookAttackMasks[square];
            foreach (var occupancy in Subsets(mask))
            {
                var expected = RookRayWalk(square, occupancy);
                AttackTables.RookAttacks(occupancy, square)
                    .Should().Be(expected, $"PEXT rook square={square} occupancy={occupancy}");
                AttackTables.RookMagics[square].GetMoves(occupancy)
                    .Should().Be(expected, $"magic rook square={square} occupancy={occupancy}");
            }
        }
    }

    [Fact]
    public void PextAndMagicBishopAttacks_MatchRayWalk_OverAllRelevantOccupancies()
    {
        for (var square = 0; square < 64; square++)
        {
            var mask = AttackTables.BishopAttackMasks[square];
            foreach (var occupancy in Subsets(mask))
            {
                var expected = BishopRayWalk(square, occupancy);
                AttackTables.BishopAttacks(occupancy, square)
                    .Should().Be(expected, $"PEXT bishop square={square} occupancy={occupancy}");
                AttackTables.BishopMagics[square].GetMoves(occupancy)
                    .Should().Be(expected, $"magic bishop square={square} occupancy={occupancy}");
            }
        }
    }

    private static IEnumerable<ulong> Subsets(ulong mask)
    {
        // Classic subset enumeration: visits all 2^popcount(mask) subsets exactly once.
        for (var subset = mask;; subset = (subset - 1) & mask)
        {
            yield return subset;
            if (subset == 0)
            {
                yield break;
            }
        }
    }

    private static ulong RookRayWalk(int square, ulong occupancy)
    {
        ulong attacks = 0;
        var rank = square / 8;
        var file = square % 8;

        for (var f = file + 1; f < 8; f++)
        {
            var s = rank * 8 + f;
            attacks |= 1UL << s;
            if ((occupancy & (1UL << s)) != 0) break;
        }

        for (var f = file - 1; f >= 0; f--)
        {
            var s = rank * 8 + f;
            attacks |= 1UL << s;
            if ((occupancy & (1UL << s)) != 0) break;
        }

        for (var r = rank + 1; r < 8; r++)
        {
            var s = r * 8 + file;
            attacks |= 1UL << s;
            if ((occupancy & (1UL << s)) != 0) break;
        }

        for (var r = rank - 1; r >= 0; r--)
        {
            var s = r * 8 + file;
            attacks |= 1UL << s;
            if ((occupancy & (1UL << s)) != 0) break;
        }

        return attacks;
    }

    private static ulong BishopRayWalk(int square, ulong occupancy)
    {
        ulong attacks = 0;
        var rank = square / 8;
        var file = square % 8;

        for (var step = 1; ; step++)
        {
            var r = rank + step;
            var f = file + step;
            if (r > 7 || f > 7) break;
            var s = r * 8 + f;
            attacks |= 1UL << s;
            if ((occupancy & (1UL << s)) != 0) break;
        }

        for (var step = 1; ; step++)
        {
            var r = rank + step;
            var f = file - step;
            if (r > 7 || f < 0) break;
            var s = r * 8 + f;
            attacks |= 1UL << s;
            if ((occupancy & (1UL << s)) != 0) break;
        }

        for (var step = 1; ; step++)
        {
            var r = rank - step;
            var f = file + step;
            if (r < 0 || f > 7) break;
            var s = r * 8 + f;
            attacks |= 1UL << s;
            if ((occupancy & (1UL << s)) != 0) break;
        }

        for (var step = 1; ; step++)
        {
            var r = rank - step;
            var f = file - step;
            if (r < 0 || f < 0) break;
            var s = r * 8 + f;
            attacks |= 1UL << s;
            if ((occupancy & (1UL << s)) != 0) break;
        }

        return attacks;
    }
}
