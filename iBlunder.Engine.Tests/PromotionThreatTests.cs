using FluentAssertions;
using iBlunder.Engine.MoveGen;
using iBlunder.Engine.Search;
using iBlunder.Engine.Transpositions;

namespace iBlunder.Engine.Tests;

public class PromotionThreatTests
{
    [Theory]
    [InlineData("7K/8/8/8/8/8/2p5/7k b - -", "c2c1b", true)]
    [InlineData("7K/8/8/8/8/2p5/3P4/7k b - - 0 1", "c3c2", true)]
    public void IsPromotionThreat_Tests(string fen, string uciMove, bool expected)
    {
        // Given
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        var searcher = new Searcher(Array.Empty<Transposition>());
        var moves = new List<uint>();
        board.GenerateLegalMoves(moves, false);
        searcher.Init(0, board);

        var move = Assert.Single(moves.Where(m => m.ToUciMoveName() == uciMove));

        // When
        var actual = move.IsPromotionThreat();

        // Then
        actual.Should().Be(expected);
    }
}