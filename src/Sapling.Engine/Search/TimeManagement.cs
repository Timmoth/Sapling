namespace Sapling.Engine.Search;

public readonly record struct SearchTimeBudget(int SoftLimitMs, int HardLimitMs, bool Infinite)
{
    public static SearchTimeBudget InfiniteSearch => new(0, 0, true);
}

public static class TimeManagement
{
    private const long OverheadMs = 50;
    private const long MinSearchMs = 10;
    private const long ShortClockThresholdMs = 120_000;
    private static ReadOnlySpan<double> StabilityFactors => [1.8, 1.4, 1.2, 1.0, 0.9, 0.8, 0.75];

    public static SearchTimeBudget Calculate(
        int timeMs,
        int incrementMs,
        int movesToGo,
        int plyCount,
        bool hasClock,
        int moveTimeMs = -1,
        bool infinite = false)
    {
        if (infinite)
            return SearchTimeBudget.InfiniteSearch;

        if (moveTimeMs >= 0)
        {
            var moveTime = Math.Max(1, moveTimeMs);
            return new SearchTimeBudget(moveTime, moveTime, false);
        }

        if (!hasClock)
            return SearchTimeBudget.InfiniteSearch;

        var remaining = Math.Max(1, (long)timeMs - OverheadMs);
        var increment = Math.Max(0, incrementMs);
        var moves = movesToGo > 0
            ? Math.Clamp(movesToGo, 1, 500)
            : plyCount < 40 ? 30 : plyCount < 80 ? 20 : 10;

        long soft;
        if (remaining < ShortClockThresholdMs)
        {
            var allocation = remaining / moves;
            if (remaining > increment * 2)
                allocation += increment * 8 / 10;

            var minimum = Math.Min(50, remaining / 4);
            soft = Math.Max(minimum, allocation);
        }
        else
        {
            var allocation = remaining / (moves + 2) + increment * 6 / 10;
            soft = allocation * 3 / 4;
        }

        soft = Math.Clamp(soft, 1, remaining);
        var hard = Math.Clamp(remaining * 9 / 10, soft, remaining);
        return new SearchTimeBudget((int)soft, (int)hard, false);
    }

    public static int AdjustSoftLimit(
        int baseSoftLimitMs,
        int hardLimitMs,
        int depth,
        int stableBestMoves,
        int scoreDrop)
    {
        if (depth < 8 || baseSoftLimitMs <= 0)
            return baseSoftLimitMs;

        var stability = StabilityFactors[Math.Min(stableBestMoves, StabilityFactors.Length - 1)];
        var scoreStability = Math.Clamp(1 + scoreDrop / 300d, 0.8, 1.4);
        var scale = Math.Clamp(stability * scoreStability, 0.65, 1.8);
        var adjusted = (int)Math.Round(baseSoftLimitMs * scale);
        return hardLimitMs > 0 ? Math.Min(adjusted, hardLimitMs) : adjusted;
    }
}
