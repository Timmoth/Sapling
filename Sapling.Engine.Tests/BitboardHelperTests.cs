using FluentAssertions;

namespace Sapling.Engine.Tests;

public class BitboardHelperTests
{
    [Fact]
    public void ShiftTests()
    {
        for (var rank = 0; rank < 8; rank++)
        {
            for (var file = 0; file < 8; file++)
            {
                var bitboard = 1UL << (rank * 8 + file);
                var canMoveUp = rank < 7;
                var canMoveDown = rank > 0;
                var canMoveLeft = file > 0;
                var canMoveRight = file < 7;

                var leftSquare = canMoveLeft ? BitboardHelpers.RankFileToBitboard(rank, file - 1) : 0;
                var rightSquare = canMoveRight ? BitboardHelpers.RankFileToBitboard(rank, file + 1) : 0;
                var upLeft = canMoveUp && canMoveLeft ? BitboardHelpers.RankFileToBitboard(rank + 1, file - 1) : 0;
                var upSquare = canMoveUp ? BitboardHelpers.RankFileToBitboard(rank + 1, file) : 0;
                var upRight = canMoveUp && canMoveRight ? BitboardHelpers.RankFileToBitboard(rank + 1, file + 1) : 0;
                var downLeft = canMoveDown && canMoveLeft ? BitboardHelpers.RankFileToBitboard(rank - 1, file - 1) : 0;
                var downSquare = canMoveDown ? BitboardHelpers.RankFileToBitboard(rank - 1, file) : 0;
                var downRight = canMoveDown && canMoveRight
                    ? BitboardHelpers.RankFileToBitboard(rank - 1, file + 1)
                    : 0;

                bitboard.ShiftUp().PeekLSB().Should().Be(upSquare.PeekLSB());
                bitboard.ShiftDown().PeekLSB().Should().Be(downSquare.PeekLSB());
                bitboard.ShiftLeft().PeekLSB().Should().Be(leftSquare.PeekLSB());
                bitboard.ShiftRight().PeekLSB().Should().Be(rightSquare.PeekLSB());
                bitboard.ShiftUpLeft().PeekLSB().Should().Be(upLeft.PeekLSB());
                bitboard.ShiftUpRight().PeekLSB().Should().Be(upRight.PeekLSB());
                bitboard.ShiftDownLeft().PeekLSB().Should().Be(downLeft.PeekLSB());
                bitboard.ShiftDownRight().PeekLSB().Should().Be(downRight.PeekLSB());
            }
        }
    }
}