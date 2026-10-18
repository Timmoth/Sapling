using FluentAssertions;
using Sapling.Engine;
using Xunit;

namespace Sapling.Engine.Tests;

public unsafe class NnueTrainerLayoutTests
{
    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1")]
    [InlineData("2k5/1p3q2/8/3N4/4p3/8/R5P1/6K1 w - - 0 1")]
    [InlineData("6k1/1P5r/8/3n4/4P3/8/2Q3p1/5K2 b - - 0 1")]
    public void BulletCanonicalFeaturesMatchSaplingRuntime(string fen)
    {
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        var ourKing = board.WhiteToMove ? board.WhiteKingSquare : board.BlackKingSquare ^ 56;
        var oppKing = board.WhiteToMove ? board.BlackKingSquare ^ 56 : board.WhiteKingSquare;
        var ourMirror = (ourKing & 7) >= 4 ? 7 : 0;
        var oppMirror = (oppKing & 7) >= 4 ? 7 : 0;
        var ourWhiteView = board.WhiteToMove;

        ((int)NnueOracle.OracleBucketLayout[ourKing]).Should().Be(
            NnueOracle.OracleInputBucket(&board, ourWhiteView));
        ((int)NnueOracle.OracleBucketLayout[oppKing]).Should().Be(
            NnueOracle.OracleInputBucket(&board, !ourWhiteView));

        foreach (var piece in NnueOracle.PieceTypes)
        {
            var occupied = board.Occupancy[piece];
            while (occupied != 0)
            {
                var square = System.Numerics.BitOperations.TrailingZeroCount(occupied);
                occupied &= occupied - 1;
                var originalWhite = (piece & 1) == 0;
                var canonicalWhite = board.WhiteToMove == originalWhite;
                var canonicalSquare = board.WhiteToMove ? square : square ^ 56;
                var type = (piece >> 1) - (originalWhite ? 1 : 0);
                var stm = (canonicalWhite ? 0 : 384) + type * 64 + canonicalSquare;
                var ntm = (canonicalWhite ? 384 : 0) + type * 64 + (canonicalSquare ^ 56);

                (stm ^ ourMirror).Should().Be(
                    NnueOracle.RawFeatureIndex(piece, square, ourMirror, ourWhiteView));
                (ntm ^ oppMirror).Should().Be(
                    NnueOracle.RawFeatureIndex(piece, square, oppMirror, !ourWhiteView));
            }
        }
    }
}
