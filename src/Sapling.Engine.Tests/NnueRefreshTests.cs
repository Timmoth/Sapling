using System.Runtime.InteropServices;
using Sapling.Engine.Evaluation;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Tests;

public unsafe class NnueRefreshTests
{
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, true)]
    [InlineData(4, 7, false)]
    [InlineData(32, 32, true)]
    [InlineData(64, 64, false)]
    public void DeltaTilesMatchScalarWrappingAndBothOutputs(int addCount, int subCount, bool aliasSource)
    {
        const int width = NnueWeights.Layer1Size;
        var data = MemoryHelpers.Allocate<short>(width * 131);
        try
        {
            var source = data;
            var cache = aliasSource ? source : data + width;
            var destination = data + 2 * width;
            var adds = stackalloc short*[64];
            var subs = stackalloc short*[64];
            var random = new Random(20261020);
            for (var i = 0; i < width * 131; i++)
                data[i] = (short)random.Next(short.MinValue, short.MaxValue + 1);
            for (var i = 0; i < 64; i++)
            {
                adds[i] = data + (3 + i) * width;
                subs[i] = data + (67 + i) * width;
            }
            var expected = new short[width];
            for (var i = 0; i < width; i++)
            {
                var value = source[i];
                for (var f = 0; f < addCount; f++) value = unchecked((short)(value + adds[f][i]));
                for (var f = 0; f < subCount; f++) value = unchecked((short)(value - subs[f][i]));
                expected[i] = value;
            }
            Searcher.ApplyRefreshDeltas(source, cache, destination, adds, addCount, subs, subCount);
            for (var i = 0; i < width; i++)
            {
                Assert.Equal(expected[i], cache[i]);
                Assert.Equal(expected[i], destination[i]);
            }
        }
        finally { MemoryHelpers.FreeAligned(data); }
    }

    [Fact]
    public void ColdAndCachedRefreshesMatchIndependentOracle()
    {
        var tt = MemoryHelpers.Allocate<Transposition>(1024);
        using var game = GameState.InitialState();
        using var searcher = new Searcher(tt, 1024);
        try
        {
            // Revisit every fixture without clearing cache state, exercising both paths,
            // including positions with very different feature sets in the same bucket.
            string[] fens = [
                "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                "4k3/8/8/8/8/8/8/4K3 w - - 0 1",
                "4k3/pppppppp/8/8/8/8/PPPPPPPP/4K3 b - - 0 1",
                "3k4/1q6/8/3N4/8/8/5R2/3K4 w - - 0 1",
                "7k/8/8/8/8/8/K7/8 w - - 0 1",
                "k7/8/8/8/8/8/7K/8 b - - 0 1",
                "8/8/k7/8/8/7K/8/8 w - - 0 1",
                "8/8/7k/8/8/K7/8/8 b - - 0 1"];
            for (var repeat = 0; repeat < 3; repeat++)
            foreach (var fen in fens)
            {
                game.ResetToFen(fen);
                searcher.Boards[0] = game.Board;
                var board = searcher.Boards;
                var metadata = searcher.Accumulators;
                metadata->UpdateTo(board);
                searcher.RefreshAccumulatorWhite(board, metadata, 0);
                searcher.RefreshAccumulatorBlack(board, metadata, 0);
                var white = NnueOracle.OracleAccumulator(board, true);
                var black = NnueOracle.OracleAccumulator(board, false);
                for (var i = 0; i < NnueWeights.Layer1Size; i++)
                {
                    Assert.Equal(white[i], searcher.WhiteAccumulators[0][i]);
                    Assert.Equal(black[i], searcher.BlackAccumulators[0][i]);
                }
                Assert.Equal(NnueOracle.OracleEvalFromScratch(board), searcher.Evaluate(board, metadata, 0));
            }
        }
        finally
        {
            searcher.Dispose();
            MemoryHelpers.FreeAligned(tt);
        }
    }
}
