namespace Sapling.Engine.Search;

internal static class PruningMargins
{
    public static int ReverseFutility(int depth, int baseMargin, int rawEval, int correctedEval, float correctionScale)
    {
        var correctionMagnitude = Math.Abs((long)correctedEval - rawEval);
        var correctionMargin = (long)Math.Round(correctionMagnitude * (double)correctionScale,
            MidpointRounding.AwayFromZero);
        var margin = (long)depth * baseMargin + correctionMargin;
        return (int)Math.Clamp(margin, 0, int.MaxValue);
    }
}
