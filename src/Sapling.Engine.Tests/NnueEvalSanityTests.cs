using System.Runtime.InteropServices;
using FluentAssertions;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;
using Xunit;

namespace Sapling.Engine.Tests;

// Static-evaluation sanity for the checked-in network.
//
// These are not strength tests: they pin the handful of properties any correctly trained and
// correctly *exported* net must have. Every one of them fails loudly when the trainer and the
// runtime disagree about weight layout - a transposed output layer, a flipped feature index,
// a swapped input bucket or a sign-inverted training target all show up here immediately,
// while search-level tests can still limp through such a net by accident.
public unsafe class NnueEvalSanityTests
{
    private const int TableSize = 1 << 12;

    private static int StaticEval(string fen)
    {
        var table = MemoryHelpers.Allocate<Transposition>(TableSize);
        try
        {
            using var searcher = new Searcher(table, TableSize);
            using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
            searcher.Reset(state);
            state.Board.CloneTo(ref searcher.Boards[0]);
            // The cached-eval fast path must never short-circuit the kernel under test.
            NativeMemory.Clear(searcher.Accumulators, (nuint)sizeof(AccumulatorState));
            searcher.FillInitialAccumulators(searcher.Boards, searcher.Accumulators);
            return searcher.Evaluate(searcher.Boards, searcher.Accumulators, 0);
        }
        finally
        {
            MemoryHelpers.FreeAligned(table);
        }
    }

    [Fact]
    public void StartingPosition_IsNearlyBalanced()
    {
        StaticEval("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")
            .Should().BeInRange(-100, 200, "the start position is close to equal with a small tempo edge");
    }

    // Side to move is up the named piece; the mirrored FEN is the same position with colours
    // reversed, so the two evaluations must be opposites of comparable size.
    [Theory]
    [InlineData("rnb1kbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNB1KBNR b KQkq - 0 1", 600)]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1",
                "r5k1/5ppp/8/8/8/8/5PPP/6K1 b - - 0 1", 300)]
    [InlineData("8/5k2/8/8/8/8/3QK3/8 w - - 0 1",
                "8/3qk3/8/8/8/8/5K2/8 b - - 0 1", 600)]
    public void SideToMoveUpMaterial_IsClearlyWinning(string fen, string mirrored, int minimum)
    {
        var eval = StaticEval(fen);
        var mirroredEval = StaticEval(mirrored);

        eval.Should().BeGreaterThan(minimum, $"{fen} is a large material advantage for the side to move");
        mirroredEval.Should().BeGreaterThan(minimum, $"{mirrored} is the same advantage with colours reversed");
    }

    // The colour-reversed position is a different position to the net (pawns are not
    // vertically symmetric), but a sane evaluation must still agree to within a pawn.
    [Theory]
    [InlineData("8/8/8/4k3/8/8/4P3/4K3 w - - 0 1", "4k3/4p3/8/8/4K3/8/8/8 b - - 0 1")]
    [InlineData("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 0 1",
                "rnbqk2r/pppp1ppp/5n2/2b1p3/4P3/2N5/PPPP1PPP/R1BQKBNR w KQkq - 0 1")]
    public void ColourReversedPositions_EvaluateAlike(string fen, string mirrored)
    {
        StaticEval(fen).Should().BeCloseTo(StaticEval(mirrored), 100,
            "the same position with colours reversed must evaluate the same for the side to move");
    }

    // A symmetric position has identical accumulators for both perspectives, so the only thing
    // the net can express is a side-to-move bonus. A wrongly laid out output layer turns this
    // into a large arbitrary offset.
    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1")]
    [InlineData("4k3/pppppppp/8/8/8/8/PPPPPPPP/4K3 w - - 0 1")]
    public void SymmetricPositions_AreNearZero(string fen)
    {
        StaticEval(fen).Should().BeInRange(-200, 300, "a materially and positionally symmetric position");
    }

    // Monotonicity across the whole piece range, which also walks every output bucket.
    [Fact]
    public void AddingMaterialForTheSideToMove_NeverLowersTheEvaluation()
    {
        string[] ladder =
        {
            "4k3/8/8/8/8/8/8/4K3 w - - 0 1",
            "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1",
            "4k3/8/8/8/8/8/3PP3/4K3 w - - 0 1",
            "4k3/8/8/8/8/8/3PP3/3NK3 w - - 0 1",
            "4k3/8/8/8/8/8/3PP3/3NKB2 w - - 0 1",
            "4k3/8/8/8/8/8/3PP3/3NKBR1 w - - 0 1",
            "4k3/8/8/8/8/8/3PP3/2QNKBR1 w - - 0 1",
        };

        var evals = Array.ConvertAll(ladder, StaticEval);
        for (var i = 1; i < evals.Length; i++)
        {
            evals[i].Should().BeGreaterThan(evals[i - 1],
                $"adding material for the side to move must not lower the evaluation ({ladder[i]})");
        }
    }
}
