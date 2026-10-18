using Sapling.Engine.DataGen;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Tests;

public unsafe class SearchTerminalRegressionTests
{
    private const int TableSize = 1024;

    [Theory]
    [InlineData("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1", false)] // stalemate
    [InlineData("8/qb6/B7/K1k5/8/1qn5/8/8 w - - 0 1", false)] // stalemate with pseudo-legal but illegal pinned capture
    [InlineData("7k/6Q1/6K1/8/8/8/8/8 b - - 100 51", true)] // mate at 50-move threshold
    [InlineData("4k3/8/8/8/8/8/4R3/4K3 b - - 100 51", false)] // check with a legal evasion
    [InlineData("4k3/4r3/8/8/8/8/8/4K3 w - - 100 51", false)] // mirrored check with an evasion
    [InlineData("kn6/1B6/1K6/8/8/8/8/8 b - - 0 1", true)] // K+B vs K+N mate
    public void Quiescence_ReturnsCorrectTerminalScore(string fen, bool isMate)
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
        var table = Bench.AllocateTranspositions(TableSize);
        try
        {
            using var searcher = new Searcher(table, TableSize);
            Prepare(searcher, state);
            var score = searcher.QuiescenceSearch(searcher.Boards, searcher.Accumulators, 1,
                Constants.MinScore, Constants.MaxScore);

            if (isMate)
            {
                Assert.True(MoveScoring.IsMateScore(score));
                Assert.Equal(-Constants.ImmediateMateScore + 1, score);
            }
            else
            {
                Assert.Equal(0, score);
            }
        }
        finally
        {
            MemoryHelpers.FreeAligned(table);
        }
    }

    [Theory]
    [InlineData("7k/6Q1/6K1/8/8/8/8/8 b - - 100 51", true)]
    [InlineData("4k3/8/8/8/8/8/4R3/4K3 b - - 100 51", false)]
    [InlineData("4k3/4r3/8/8/8/8/8/4K3 w - - 100 51", false)]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 100 51", false)]
    [InlineData("kn6/1B6/1K6/8/8/8/8/8 b - - 0 1", true)]
    public void MainSearch_DoesNotLetDrawShortcutsHideMate(string fen, bool isMate)
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
        var table = Bench.AllocateTranspositions(TableSize);
        try
        {
            using var searcher = new Searcher(table, TableSize);
            Prepare(searcher, state);
            var score = searcher.NegaMaxSearch(searcher.Boards, searcher.Accumulators,
                depthFromRoot: 1, depth: 1, Constants.MinScore, Constants.MaxScore);

            if (isMate)
            {
                Assert.True(MoveScoring.IsMateScore(score));
                Assert.Equal(-Constants.ImmediateMateScore + 1, score);
            }
            else
            {
                Assert.Equal(0, score);
            }
        }
        finally
        {
            MemoryHelpers.FreeAligned(table);
        }
    }

    [Theory]
    [InlineData("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1", false)]
    [InlineData("7k/6Q1/6K1/8/8/8/8/8 b - - 0 1", false)]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", true)]
    public void HasAnyLegalMove_DistinguishesStalemateMateAndOrdinaryPlay(string fen, bool expected)
    {
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        Assert.Equal(expected, board.HasAnyLegalMove());
    }

    [Fact]
    public void NegativeDepth_SearchesQuiescenceWithoutDepthScaledPruning()
    {
        using var state = GameState.InitialState();
        var table = Bench.AllocateTranspositions(TableSize);
        try
        {
            using var searcher = new Searcher(table, TableSize);
            Prepare(searcher, state);
            searcher.Boards[1] = searcher.Boards[0];
            searcher.Accumulators[1] = searcher.Accumulators[0];
            Searcher.SimdCopy(searcher.WhiteAccumulators[1], searcher.WhiteAccumulators[0]);
            Searcher.SimdCopy(searcher.BlackAccumulators[1], searcher.BlackAccumulators[0]);

            var eval = searcher.Evaluate(searcher.Boards + 1, searcher.Accumulators + 1, 1);
            var expected = searcher.QuiescenceSearch(searcher.Boards + 1,
                searcher.Accumulators + 1, 1, eval, eval + 1);
            System.Runtime.InteropServices.NativeMemory.Clear(table,
                (nuint)(TableSize * sizeof(Transposition)));
            var actual = searcher.NegaMaxSearch(searcher.Boards + 1,
                searcher.Accumulators + 1, 1, -1, eval, eval + 1);

            Assert.Equal(expected, actual);
        }
        finally
        {
            MemoryHelpers.FreeAligned(table);
        }
    }

    [Fact]
    public void DepthZeroReverseFutility_DoesNotTurnStalemateIntoAWin()
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(
            "7k/5Q2/6K1/8/8/8/8/8 b - - 0 1"));
        var table = Bench.AllocateTranspositions(TableSize);
        try
        {
            using var searcher = new Searcher(table, TableSize);
            Prepare(searcher, state);
            var staticEval = searcher.Evaluate(searcher.Boards, searcher.Accumulators, 1);

            var score = searcher.NegaMaxSearch(searcher.Boards, searcher.Accumulators,
                depthFromRoot: 1, depth: 0, alpha: staticEval - 1, beta: staticEval);

            Assert.True(score == 0, "a depth-zero pruning shortcut must preserve the stalemate draw");
        }
        finally
        {
            MemoryHelpers.FreeAligned(table);
        }
    }

    private static void Prepare(Searcher searcher, GameState state)
    {
        searcher.Reset(state);
        state.Board.CloneTo(ref searcher.Boards[0]);
        searcher.FillInitialAccumulators(searcher.Boards, searcher.Accumulators);
    }
}
