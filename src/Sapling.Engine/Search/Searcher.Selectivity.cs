using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Search;

public partial class Searcher
{
    internal static (int Depth, int Beta) ProbCutVerificationWindow(int baseDepth, int beta, int qScore)
    {
        // Keep proven mate-range values out of the score-dependent approximation.
        var reduction = IsMateBand(qScore) ? 0 : Math.Clamp(
            (qScore - beta) / Math.Max(1, SpsaOptions.ProbCutScoreDepthScale), 0, 2);
        var depth = Math.Max(Math.Min(1, baseDepth), baseDepth - reduction);
        var margin = (baseDepth - depth) * SpsaOptions.ProbCutDepthBetaScale;
        return (depth, Math.Min(beta + margin,
            TranspositionTableExtensions.PositiveCheckmateDetectionLimit - 1));
    }
}
