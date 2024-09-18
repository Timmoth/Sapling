using FluentAssertions;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

public class PerftTests
{
    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 5, 4_865_609)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -", 4, 4_085_603)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - -", 5, 674_624)]
    [InlineData("r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1", 4, 422_333)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 4, 2_103_487)]
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 4, 3_894_594)]
    public void PerftResults(string fen, int depth, ulong expectedNodes)
    {
        // Given
        var board = BoardStateExtensions.CreateBoardFromFen(fen);

        // When
        var result = board.Data.PerftRootSequential(depth);

        // Then
        ulong sum = 0;
        foreach (var (nodes, move) in result)
        {
            sum += nodes;
        }

        sum.Should().Be(expectedNodes);
    }
}