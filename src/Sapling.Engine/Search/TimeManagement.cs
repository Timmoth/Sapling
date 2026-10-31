namespace Sapling.Engine.Search;

public readonly record struct SearchTimeBudget(int SoftLimitMs, int HardLimitMs, bool Infinite)
{
    public static SearchTimeBudget InfiniteSearch => new(0, 0, true);
}

public static class TimeManagement
{
    // Keep the established 50 ms reserve while there is time to think, then ramp it to 100 ms
    // as the clock approaches zero. A fixed 100 ms reserve reduced bullet strength; a fixed
    // 50 ms reserve produced 1 ms flag overruns in loaded gauntlets.
    private const long MinOverheadMs = 50;
    private const long MaxOverheadMs = 100;
    private const long OverheadRampMs = 1_000;
    private const long MinSearchMs = 10;
    private const long ShortClockThresholdMs = 120_000;
    private const long MaxHardToSoftMultiple = 4;
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

        var remaining = Math.Max(1, (long)timeMs - CalculateOverhead(timeMs));
        var increment = Math.Max(0, incrementMs);
        var moves = movesToGo > 0
            ? Math.Clamp(movesToGo, 1, 500)
            : plyCount < 40 ? 40 : plyCount < 80 ? 30 : 20;

        long soft;
        if (remaining < ShortClockThresholdMs)
        {
            var allocation = remaining / moves;
            if (remaining > increment * 2)
                allocation += increment * 8 / 10;

            var minimum = Math.Min(MinSearchMs, remaining / 4);
            soft = Math.Max(minimum, allocation);
        }
        else
        {
            var allocation = remaining / (moves + 2) + increment * 6 / 10;
            soft = allocation * 3 / 4;
        }

        soft = Math.Clamp(soft, 1, remaining);
        // A single deep iteration can run past the soft limit before iterative deepening
        // checks it again. Keep the hard deadline tied to this move's allocation so that
        // one such iteration cannot consume most of the player's remaining clock.
        var hard = Math.Clamp(Math.Min(remaining * 9 / 10, soft * MaxHardToSoftMultiple), soft, remaining);
        return new SearchTimeBudget((int)soft, (int)hard, false);
    }

    private static long CalculateOverhead(int timeMs)
    {
        var clock = Math.Clamp((long)timeMs, 0, OverheadRampMs);
        var ramp = (OverheadRampMs - clock) * (MaxOverheadMs - MinOverheadMs) / OverheadRampMs;
        return MinOverheadMs + ramp;
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
