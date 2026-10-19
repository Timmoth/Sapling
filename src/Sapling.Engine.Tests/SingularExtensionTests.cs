using Sapling.Engine.DataGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Tests;

// Regression tests for singular, double and negative extensions.
//
// Every bug found while building this feature failed *silently*. The search kept returning
// correct scores and the whole suite kept passing, but the extension never fired, so the
// feature was dead code that only cost nodes. Three separate defects all presented the same
// way, and only instrumentation distinguished them:
//
//   1. the gate read a transposition score that is only populated at ttDepth >= depth, which
//      is stricter than the ttDepth >= depth - 3 this feature needs, so it never passed;
//   2. the move loop never skipped the excluded move, so the "verification" search still
//      contained the move it was supposed to be testing;
//   3. the verification search took a transposition cutoff from the very entry it was
//      testing - an entry computed *with* the excluded move - so nothing looked singular.
//
// Each of those leaves the counters below at zero, which is what these tests assert against.
public unsafe class SingularExtensionTests
{
    private const int TableSize = 1 << 16;

    private static readonly string[] Positions =
    {
        "r1bq1rk1/pp2nppp/4n3/3pP3/1b1P4/2NB1N2/PP3PPP/R1BQK2R w KQ - 0 1",
        "r3k2r/pp1bbppp/2n1pn2/2pp4/3P4/2N1PN2/PP1BBPPP/R2Q1RK1 w kq - 0 1",
        "2r3k1/5ppp/p7/1p1q4/3P4/P4Q2/1P3PPP/2R3K1 w - - 0 1",
    };

    private static (long verifications, long extensions, long negative, long nodes) RunPositions(int depth)
    {
        var table = Bench.AllocateTranspositions(TableSize);
        try
        {
            using var searcher = new Searcher(table, TableSize);
            long nodes = 0;
            foreach (var fen in Positions)
            {
                using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
                var result = searcher.Search(state, searcher.Stop, depthLimit: depth, threadId: 0);
                nodes += result.nodes;
            }

            return (searcher.SingularVerifications, searcher.SingularExtensions,
                searcher.SingularNegativeExtensions, nodes);
        }
        finally
        {
            MemoryHelpers.FreeAligned(table);
        }
    }

    [Fact]
    public void VerificationSearchesActuallyRun()
    {
        var (verifications, _, _, _) = RunPositions(12);

        // Defect 1 leaves this at zero: the gate is never satisfied.
        Assert.True(verifications > 0,
            "the singular gate must be reachable at depth 12, otherwise the feature is inert");
    }

    [Fact]
    public void VerificationSearchesChangeExtensions()
    {
        var (verifications, extensions, negative, _) = RunPositions(12);

        // Defects 2 and 3 both leave this at zero: verification runs, but the excluded move
        // is still in the search (2) or its own transposition entry answers the question (3),
        // so no move is ever found singular and no extension is ever applied.
        Assert.True(extensions + negative > 0,
            $"verification searches that never change an extension are pure overhead " +
            $"({verifications} verifications, {extensions} positive, {negative} negative)");
    }

    [Fact]
    public void NegativeExtensionsFire()
    {
        var (_, _, negative, _) = RunPositions(12);

        // Negative extensions are what pays for the verification searches; without them the
        // feature costs roughly twice as many nodes for the same benefit.
        Assert.True(negative > 0, "negative extensions must fire, they offset the verification cost");
    }

    [Fact]
    public void VerificationRateStaysBounded()
    {
        var (verifications, _, _, nodes) = RunPositions(12);

        // The verification search is the entire cost of this feature. If the gate loosens,
        // this is the number that moves first. The bound is deliberately generous - it is a
        // guard against a regression, not a tuning target.
        var rate = 100.0 * verifications / nodes;
        Assert.True(rate < 10.0,
            $"verification searches ran at {rate:F2}% of nodes; the gate has loosened");
    }
}
