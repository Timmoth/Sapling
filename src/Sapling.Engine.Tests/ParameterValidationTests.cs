using FluentAssertions;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Tests;

/// <summary>
/// Contract tests for SpsaTuner.SetParameterValue: invariant parsing, rejection of malformed,
/// nonfinite and out-of-range input WITHOUT mutating state, and acceptance of valid values.
/// Tunable static fields exist only under the OpenBench build, so these compile/run there.
/// </summary>
#if OpenBench
public class ParameterValidationTests
{
    private static string[] Cmd(string name, string value) =>
        new[] { "setoption", "name", name, "value", value };

    [Fact]
    public void RejectsOutOfRangeInt_LeavesStateUnchanged()
    {
        var before = SpsaOptions.RazorMarginB; // bounds 250..400
        SpsaTuner.SetParameterValue(Cmd("RazorMarginB", "500"));
        SpsaOptions.RazorMarginB.Should().Be(before, "out-of-range rejected, no mutation");
        SpsaTuner.SetParameterValue(Cmd("RazorMarginB", "10"));
        SpsaOptions.RazorMarginB.Should().Be(before);
    }

    [Fact]
    public void AcceptsInRangeInt()
    {
        SpsaTuner.SetParameterValue(Cmd("RazorMarginB", "300"));
        SpsaOptions.RazorMarginB.Should().Be(300);
        SpsaTuner.SetParameterValue(Cmd("RazorMarginB", "378")); // restore default
    }

    [Fact]
    public void RejectsNonFiniteFloat()
    {
        var before = SpsaOptions.LateMoveReductionB;
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "NaN"));
        SpsaOptions.LateMoveReductionB.Should().Be(before);
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "Infinity"));
        SpsaOptions.LateMoveReductionB.Should().Be(before);
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "-Infinity"));
        SpsaOptions.LateMoveReductionB.Should().Be(before);
    }

    [Fact]
    public void RejectsOutOfRangeFloatDenominator()
    {
        // LateMoveReductionB is a table denominator with advertised min 0.1; zero must be rejected,
        // not coerced to a different value.
        var before = SpsaOptions.LateMoveReductionB;
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "0"));
        SpsaOptions.LateMoveReductionB.Should().Be(before);
    }

    [Fact]
    public void MalformedAndUnknownAreNoOps()
    {
        var before = SpsaOptions.RazorMarginB;
        SpsaTuner.SetParameterValue(new[] { "setoption", "name", "RazorMarginB" }); // too short
        SpsaTuner.SetParameterValue(new[] { "setoption", "name", "NoSuchParam", "value", "1" });
        SpsaOptions.RazorMarginB.Should().Be(before);
    }

    [Fact]
    public void InvariantParsingAcceptsDotDecimal()
    {
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "2.5"));
        SpsaOptions.LateMoveReductionB.Should().Be(2.5f);
        SpsaTuner.SetParameterValue(Cmd("LateMoveReductionB", "2.7831153"));
    }
}
#endif
