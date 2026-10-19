using Sapling.Engine.Tuning;
using Xunit;

namespace Sapling.Engine.Tests;

// Regression tests for the SPSA input line the engine emits for OpenBench.
//
// Background: LateMoveReductionHistoryScale was declared with range [0, 0.0005]. The step-size
// formula floors at 0.01, so the emitted perturbation was 20x the parameter's entire legal range.
// Every SPSA iteration clamped both arms to the bounds, so that parameter learned nothing and its
// noise polluted the shared gradient estimate for the six other parameters in the session. The
// session ran 600 iterations / 9,606 games before anyone noticed, because nothing reports it.
public class SpsaParameterFormatTests
{
    private static SpsaParameter Param(string type, string def, string min, string max) =>
        new() { Name = "Test", Type = type, DefaultValue = def, MinValue = min, MaxValue = max };

    [Theory]
    [InlineData("float", "0.0001", "0", "0.0005")] // the parameter that actually broke
    [InlineData("float", "0.5", "0.4", "0.6")]
    public void FormatParameter_rejects_a_range_too_narrow_for_the_step_formula(
        string type, string def, string min, string max)
    {
        Assert.Throws<InvalidOperationException>(
            () => SpsaTuner.FormatParameter(Param(type, def, min, max)));
    }

    [Theory]
    [InlineData("int", "8", "4", "12")]
    [InlineData("int", "16", "4", "64")]
    [InlineData("int", "100", "0", "500")] // the rescaled history-scale parameter
    [InlineData("float", "1.315961", "0.1", "10.0")]
    public void Emitted_step_size_stays_a_small_fraction_of_the_range(
        string type, string def, string min, string max)
    {
        var parts = SpsaTuner.FormatParameter(Param(type, def, min, max)).Split(',');
        var lo = double.Parse(min);
        var hi = double.Parse(max);
        var stepSize = double.Parse(parts[5]);
        var learningRate = double.Parse(parts[6]);

        // A perturbation must be small relative to the range, or both arms clamp to the bounds.
        Assert.True(stepSize <= (hi - lo) / 4.0,
            $"step {stepSize} is too large for range [{lo}, {hi}]");
        Assert.True(stepSize > 0);
        // The learning rate is inversely proportional to the step, so a tiny step explodes it.
        Assert.InRange(learningRate, 0.0, 1.0);
    }

    [Theory]
    [InlineData("0", "10")]   // every depth gate: range/40 = 0.25
    [InlineData("4", "12")]   // SingularMinDepth
    [InlineData("1", "6")]    // SingularTtDepthMargin
    [InlineData("0", "6")]    // ProbCutMinDepth
    public void Int_perturbation_is_never_smaller_than_half_a_unit(string min, string max)
    {
        // An int option is rounded before it reaches the engine. A step under 0.5 leaves both
        // SPSA arms on the same integer, so the two engines are identical and the iteration
        // measures nothing. Nine parameters were in this state across four live sessions.
        var parts = SpsaTuner.FormatParameter(Param("int", min, min, max)).Split(',');
        Assert.True(double.Parse(parts[5]) >= 0.5,
            $"int range [{min}, {max}] emits step {parts[5]}, which cannot move the value");
    }

    [Fact]
    public void Rescaled_history_scale_reproduces_the_original_float_value()
    {
        // 100 micros must be exactly the 0.0001f the search used before the rescale,
        // so the default build is bit-identical.
        Assert.Equal(0.0001f, SpsaOptions.LateMoveReductionHistoryScaleMicros / 1_000_000f);
    }
}
