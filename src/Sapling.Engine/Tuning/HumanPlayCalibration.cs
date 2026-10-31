namespace Sapling.Engine.Tuning;

public readonly record struct HumanPlaySettings(int ModelElo, float Temperature, float DirectCaptureRiskPenalty = 0f);

/// <summary>
/// Human-play calibration checked against Lichess rapid moves and Maia-relative outcomes.
/// The map is valid only for the
/// embedded policy weights it was fitted against.
/// </summary>
public static class HumanPlayCalibration
{
    public const string CalibrationId = "lichess-rapid-balanced-human-errors-upper-v1";
    public const string StrengthExperimentId = "maia-cutechess-2026-10-balanced-upper-v1";
    public const int ExperimentalModelEloAdjustment = 0;
    public const string SourceModelSha256 = "b075580ede7c87f7bb1375189d0d894d969e02579ed346efd3f0b116bc9aee87";

    private static readonly (int DisplayElo, int ModelElo, float Temperature, float DirectCaptureRiskPenalty)[] Anchors =
    [
        (500, 500, 1f, 0.5f),
        (700, 750, 1f, 0.625f),
        (900, 1000, 1f, 0.75f),
        (1100, 1175, 1f, 0.75f),
        (1300, 1300, 1f, 0.625f),
        (1500, 1500, 1f, 0.625f),
        (1700, 1700, 1f, 0.375f),
        (1900, 1800, 1.025f, 0.75f),
        (2100, 1950, 1.05f, 0.75f),
        (2200, 2050, 1.05f, 0.75f)
    ];

    public static bool AppliesToCurrentModel
        => string.Equals(HumanMovePolicy.ModelSha256, SourceModelSha256, StringComparison.Ordinal);

    public static HumanPlaySettings ForDisplayElo(int displayElo)
    {
        if (displayElo <= 0)
            return new HumanPlaySettings(0, 1f, 0f);

        var clamped = Math.Clamp(displayElo, HumanPlay.MinElo, HumanPlay.MaxElo);
        if (!AppliesToCurrentModel)
            return new HumanPlaySettings(clamped, 1f, 0f);

        if (clamped <= Anchors[0].DisplayElo)
        {
            var first = Anchors[0];
            var next = Anchors[1];
            var slope = (next.ModelElo - first.ModelElo)
                / (float)(next.DisplayElo - first.DisplayElo);
            return new HumanPlaySettings(
                (int)MathF.Round(first.ModelElo + (clamped - first.DisplayElo) * slope)
                    + ExperimentalModelEloAdjustment,
                first.Temperature, first.DirectCaptureRiskPenalty);
        }

        for (var i = 1; i < Anchors.Length; i++)
        {
            var right = Anchors[i];
            if (clamped > right.DisplayElo) continue;
            var left = Anchors[i - 1];
            var fraction = (clamped - left.DisplayElo) / (float)(right.DisplayElo - left.DisplayElo);
            var modelElo = left.ModelElo + fraction * (right.ModelElo - left.ModelElo);
            var temperature = left.Temperature + fraction * (right.Temperature - left.Temperature);
            var riskPenalty = left.DirectCaptureRiskPenalty
                + fraction * (right.DirectCaptureRiskPenalty - left.DirectCaptureRiskPenalty);
            return new HumanPlaySettings(
                (int)MathF.Round(modelElo) + ExperimentalModelEloAdjustment,
                temperature, riskPenalty);
        }

        var last = Anchors[^1];
        return WithStrengthAdjustment(last);
    }

    private static HumanPlaySettings WithStrengthAdjustment(
        (int DisplayElo, int ModelElo, float Temperature, float DirectCaptureRiskPenalty) anchor)
        => new(anchor.ModelElo + ExperimentalModelEloAdjustment,
            anchor.Temperature, anchor.DirectCaptureRiskPenalty);
}
