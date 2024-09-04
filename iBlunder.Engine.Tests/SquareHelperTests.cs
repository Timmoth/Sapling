using FluentAssertions;

namespace iBlunder.Engine.Tests;

public class SquareHelperTests
{
    [Fact]
    public void ShiftTests()
    {
        for (var rank = 0; rank < 8; rank++)
        {
            for (var file = 0; file < 8; file++)
            {
                var square = (byte)(rank * 8 + file);

                var leftSquare = (byte)(square - 1);
                var rightSquare = (byte)(square + 1);
                var upLeft = (byte)(square + 8 - 1);
                var upSquare = (byte)(square + 8);
                var upRight = (byte)(square + 8 + 1);
                var downLeft = (byte)(square - 8 - 1);
                var downSquare = (byte)(square - 8);
                var downRight = (byte)(square - 8 + 1);

                square.ShiftUp().Should().Be(upSquare);
                square.ShiftDown().Should().Be(downSquare);
                square.ShiftLeft().Should().Be(leftSquare);
                square.ShiftRight().Should().Be(rightSquare);
                square.ShiftUpLeft().Should().Be(upLeft);
                square.ShiftUpRight().Should().Be(upRight);
                square.ShiftDownLeft().Should().Be(downLeft);
                square.ShiftDownRight().Should().Be(downRight);
            }
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 2)]
    [InlineData(3, 0, 3)]
    [InlineData(4, 0, 4)]
    [InlineData(5, 0, 5)]
    [InlineData(6, 0, 6)]
    [InlineData(7, 0, 7)]
    [InlineData(8, 1, 0)]
    [InlineData(9, 1, 1)]
    [InlineData(10, 1, 2)]
    [InlineData(11, 1, 3)]
    [InlineData(12, 1, 4)]
    [InlineData(13, 1, 5)]
    [InlineData(14, 1, 6)]
    [InlineData(15, 1, 7)]
    [InlineData(16, 2, 0)]
    [InlineData(17, 2, 1)]
    [InlineData(18, 2, 2)]
    [InlineData(19, 2, 3)]
    [InlineData(20, 2, 4)]
    [InlineData(21, 2, 5)]
    [InlineData(22, 2, 6)]
    [InlineData(23, 2, 7)]
    [InlineData(24, 3, 0)]
    [InlineData(25, 3, 1)]
    [InlineData(26, 3, 2)]
    [InlineData(27, 3, 3)]
    [InlineData(28, 3, 4)]
    [InlineData(29, 3, 5)]
    [InlineData(30, 3, 6)]
    [InlineData(31, 3, 7)]
    [InlineData(32, 4, 0)]
    [InlineData(33, 4, 1)]
    [InlineData(34, 4, 2)]
    [InlineData(35, 4, 3)]
    [InlineData(36, 4, 4)]
    [InlineData(37, 4, 5)]
    [InlineData(38, 4, 6)]
    [InlineData(39, 4, 7)]
    [InlineData(40, 5, 0)]
    [InlineData(41, 5, 1)]
    [InlineData(42, 5, 2)]
    [InlineData(43, 5, 3)]
    [InlineData(44, 5, 4)]
    [InlineData(45, 5, 5)]
    [InlineData(46, 5, 6)]
    [InlineData(47, 5, 7)]
    [InlineData(48, 6, 0)]
    [InlineData(49, 6, 1)]
    [InlineData(50, 6, 2)]
    [InlineData(51, 6, 3)]
    [InlineData(52, 6, 4)]
    [InlineData(53, 6, 5)]
    [InlineData(54, 6, 6)]
    [InlineData(55, 6, 7)]
    [InlineData(56, 7, 0)]
    [InlineData(57, 7, 1)]
    [InlineData(58, 7, 2)]
    [InlineData(59, 7, 3)]
    [InlineData(60, 7, 4)]
    [InlineData(61, 7, 5)]
    [InlineData(62, 7, 6)]
    [InlineData(63, 7, 7)]
    public void GetSquareRankAndFile(int square, int rank, int file)
    {
        square.GetRankIndex().Should().Be(rank);
        square.GetFileIndex().Should().Be(file);
    }
}