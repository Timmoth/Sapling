namespace Sapling.Engine.Search;

internal static class LateMoveReduction
{
    public static int AdjustForExactTt(int reduction, bool hasDeepExactTtEntry, float scale, int maxReduction)
    {
        var adjusted = hasDeepExactTtEntry ? (int)MathF.Round(reduction * scale) : reduction;
        return Math.Clamp(adjusted, 0, maxReduction);
    }
}
