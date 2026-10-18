
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

                Assert.Equal(upSquare.PeekLSB(), bitboard.ShiftUp().PeekLSB());
                Assert.Equal(downSquare.PeekLSB(), bitboard.ShiftDown().PeekLSB());
                Assert.Equal(leftSquare.PeekLSB(), bitboard.ShiftLeft().PeekLSB());
                Assert.Equal(rightSquare.PeekLSB(), bitboard.ShiftRight().PeekLSB());
                Assert.Equal(upLeft.PeekLSB(), bitboard.ShiftUpLeft().PeekLSB());
                Assert.Equal(upRight.PeekLSB(), bitboard.ShiftUpRight().PeekLSB());
                Assert.Equal(downLeft.PeekLSB(), bitboard.ShiftDownLeft().PeekLSB());
                Assert.Equal(downRight.PeekLSB(), bitboard.ShiftDownRight().PeekLSB());
            }
        }
    }
}