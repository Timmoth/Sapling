using Sapling.Engine.Tuning;
using Sapling.Engine.Search;
using System.Globalization;

namespace Sapling.Engine.Tests;

/// <summary>
/// Contract tests for SpsaTuner.SetParameterValue: invariant parsing, rejection of malformed,
/// nonfinite and out-of-range input WITHOUT mutating state, and acceptance of valid values.
/// Tunable static fields exist only under the OpenBench build, so these compile/run there.
/// </summary>
#if OpenBench
public unsafe class ParameterValidationTests
{
    private static string[] Cmd(string name, string value) =>
        new[] { "setoption", "name", name, "value", value };

    [Fact]
    public void OpenBenchDefaultsMatchProductionDefaults()
    {
        var defaults = new Dictionary<string, string>
        {
            [nameof(SpsaOptions.ReverseFutilityPruningMargin)] = "67",
            [nameof(SpsaOptions.ReverseFutilityPruningDepth)] = "7",
            [nameof(SpsaOptions.NullMovePruningDepth)] = "2",
            [nameof(SpsaOptions.NullMovePruningReductionA)] = "3",
            [nameof(SpsaOptions.NullMovePruningReductionB)] = "4",
            [nameof(SpsaOptions.NullMovePruningReductionC)] = "3",
            [nameof(SpsaOptions.RazorMarginA)] = "58",
            [nameof(SpsaOptions.RazorMarginB)] = "378",
            [nameof(SpsaOptions.InternalIterativeDeepeningDepth)] = "2",
            [nameof(SpsaOptions.LateMovePruningConstant)] = "8",
            [nameof(SpsaOptions.LateMoveReductionMinDepth)] = "3",
            [nameof(SpsaOptions.LateMoveReductionMinMoves)] = "2",
            [nameof(SpsaOptions.LateMoveReductionInterestingA)] = "0.188786",
            [nameof(SpsaOptions.LateMoveReductionInterestingB)] = "2.4956496",
            [nameof(SpsaOptions.LateMoveReductionA)] = "1.315961",
            [nameof(SpsaOptions.LateMoveReductionB)] = "2.7831154",
            [nameof(SpsaOptions.LateMoveReductionHistoryScale)] = "0.0001",
            [nameof(SpsaOptions.HistoryHeuristicMaxHistory)] = "9532",
            [nameof(SpsaOptions.HistoryHeuristicBonusMax)] = "406",
            [nameof(SpsaOptions.HistoryHeuristicBonusCoeff)] = "83",
            [nameof(SpsaOptions.MoveOrderingBestMoveBias)] = "239130",
            [nameof(SpsaOptions.MoveOrderingEnPassantMoveBias)] = "78671",
            [nameof(SpsaOptions.MoveOrderingWinningCaptureBias)] = "134191",
            [nameof(SpsaOptions.MoveOrderingLosingCaptureBias)] = "16845",
            [nameof(SpsaOptions.MoveOrderingPromoteBias)] = "46577",
            [nameof(SpsaOptions.MoveOrderingCapturePromoteBias)] = "35558",
            [nameof(SpsaOptions.MoveOrderingKillerABias)] = "66845",
            [nameof(SpsaOptions.MoveOrderingCounterMoveBias)] = "87003",
            [nameof(SpsaOptions.InterestingNegaMaxMoveScore)] = "40775",
            [nameof(SpsaOptions.InterestingQuiescenceMoveScore)] = "35432",
            [nameof(SpsaOptions.ProbCutBetaMargin)] = "220",
            [nameof(SpsaOptions.ProbCutMinDepth)] = "3",
            [nameof(SpsaOptions.AsperationWindowA)] = "37",
            [nameof(SpsaOptions.AsperationWindowB)] = "59",
            [nameof(SpsaOptions.AsperationWindowC)] = "279",
            [nameof(SpsaOptions.AsperationWindowD)] = "847",
            [nameof(SpsaOptions.AsperationWindowE)] = "2785"
        };

        Assert.Equal(defaults.Count, SpsaTuner.TuningParameters.Count());
        foreach (var (name, value) in defaults)
            Assert.True(SpsaTuner.TuningParameters[name.ToLowerInvariant()].DefaultValue == value, name);
    }

    [Fact]
    public void RejectsOutOfRangeInt_LeavesStateUnchanged()
    {
        var before = SpsaOptions.RazorMarginB; // bounds 250..400
        SpsaTuner.SetParameterValue(Cmd("RazorMarginB", "500"));
        Assert.True(SpsaOptions.RazorMarginB == before, "out-of-range rejected, no mutation");
        SpsaTuner.SetParameterValue(Cmd("RazorMarginB", "10"));
        Assert.Equal(before, SpsaOptions.RazorMarginB);
    }

    [Fact]
    public void AcceptsInRangeInt()
    {
        SpsaTuner.SetParameterValue(Cmd("RazorMarginB", "300"));
        Assert.Equal(300, SpsaOptions.RazorMarginB);
        SpsaTuner.SetParameterValue(Cmd("RazorMarginB", "378")); // restore default
    }

    [Fact]
    public void RejectsNonFiniteFloat()
    {
        var before = SpsaOptions.LateMoveReductionB;
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "NaN"));
        Assert.Equal(before, SpsaOptions.LateMoveReductionB);
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "Infinity"));
        Assert.Equal(before, SpsaOptions.LateMoveReductionB);
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "-Infinity"));
        Assert.Equal(before, SpsaOptions.LateMoveReductionB);
    }

    [Fact]
    public void RejectsOutOfRangeFloatDenominator()
    {
        // LateMoveReductionB is a table denominator with advertised min 0.1; zero must be rejected,
        // not coerced to a different value.
        var before = SpsaOptions.LateMoveReductionB;
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "0"));
        Assert.Equal(before, SpsaOptions.LateMoveReductionB);
    }

    [Fact]
    public void RejectsZeroNullMoveDenominator()
    {
        var before = SpsaOptions.NullMovePruningReductionB;
        Assert.False(SpsaTuner.SetParameterValue(Cmd("NullMovePruningReductionB", "0")));
        Assert.Equal(before, SpsaOptions.NullMovePruningReductionB);
    }

    [Fact]
    public void MalformedAndUnknownAreNoOps()
    {
        var before = SpsaOptions.RazorMarginB;
        SpsaTuner.SetParameterValue(new[] { "setoption", "name", "RazorMarginB" }); // too short
        SpsaTuner.SetParameterValue(new[] { "setoption", "name", "NoSuchParam", "value", "1" });
        Assert.Equal(before, SpsaOptions.RazorMarginB);
    }

    [Fact]
    public void InvariantParsingAcceptsDotDecimal()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "2.5"));
            Assert.Equal(2.5f, SpsaOptions.LateMoveReductionB);
            Assert.Contains(SpsaTuner.PrintSPSAParams(), x => x.Contains("2.5"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "2.7831153"));
        }
    }

    [Fact]
    public void ValidUpdatesRefreshDerivedTables()
    {
        SpsaTuner.SetParameterValue(Cmd("NullMovePruningReductionB", "5"));
        Assert.Equal(4, SpsaOptions.NullMovePruningReductionTable[10]);
        SpsaTuner.SetParameterValue(Cmd("NullMovePruningReductionB", "4"));

        SpsaTuner.SetParameterValue(Cmd("HistoryHeuristicBonusMax", "450"));
        Assert.Equal(450, HistoryHeuristicExtensions.BonusTable[10]);
        SpsaTuner.SetParameterValue(Cmd("HistoryHeuristicBonusMax", "406"));

        var before = SpsaOptions.LateMovePruningReductionTable[10 * 218 + 10];
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionA", "2"));
        Assert.Equal((int)Math.Round(2 + Math.Log(10) * Math.Log(10) / SpsaOptions.LateMoveReductionB), SpsaOptions.LateMovePruningReductionTable[10 * 218 + 10]);
        Assert.NotEqual(before, SpsaOptions.LateMovePruningReductionTable[10 * 218 + 10]);
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionA", "1.315961"));
    }
}
#endif
