using System;

namespace Sapling.Engine.Tuning;

/// <summary>
/// Optional human-like move sampling. Disabled by default; Elo 0 always picks the best move.
/// </summary>
public static class HumanPlay
{
    public static bool Enabled { get; private set; }
    public static int Elo { get; private set; }

    /// <summary>Search depth used to rank root moves.</summary>
    public static int RankDepth { get; set; } = 8;

    // Fit from ranked human games.
    private const double TempMax = 230.0;
    private const double EloCentre = 2374.0;
    private const double EloWidth = 1095.0;
    private const int MaxElo = 3000;
    public const int GapCap = 800;

    // Fitted move-type and piece priors.
    private const double WCapture = 1.649;
    private const double WCaptureElo = -0.221;
    private const double WCastle = 2.092;
    private const double WPromo = 1.032;
    private const double WCheck = 0.647;
    private const double WPawn = 0.186;
    private const double WKnight = 0.515;
    private const double WBishop = 0.064;
    private const double WRook = -0.028;
    private const double WQueen = -0.144;

    public static void SetElo(int elo)
    {
        Elo = Math.Max(0, elo);
        Enabled = Elo > 0;
    }

    /// <summary>Boltzmann temperature (centipawns) for a target Elo. Monotonically decreasing.</summary>
    public static double TemperatureForElo(int elo)
    {
        if (elo <= 0) return 0.0;
        if (elo >= MaxElo) return 0.0;
        return TempMax / (1.0 + Math.Exp((elo - EloCentre) / EloWidth));
    }

    /// <summary>
    /// Samples a root-move index to play. Score for candidate i = -min(gap_i, GapCap)/T(Elo) plus a
    /// move-type/piece prior; sampled by softmax. <paramref name="scores"/> must be ranked best-first
    /// and <paramref name="feats"/> the matching feature bitmask from RankRootMoves. Returns 0 (the
    /// best move) when disabled, at max Elo, or when there is at most one move.
    /// </summary>
    public static int PickRank(Random rng, int[] scores, int[] feats)
    {
        if (!Enabled || scores is null || feats is null || scores.Length <= 1)
            return 0;
        if (Elo >= MaxElo)
            return 0;

        var t = TemperatureForElo(Elo);
        if (t <= 1e-6)
            return 0;

        var elo = (Elo - 1500.0) / 1000.0;
        var best = scores[0];
        var n = scores.Length;
        var z = new double[n];
        var max = double.NegativeInfinity;
        for (var i = 0; i < n; i++)
        {
            var gap = (double)(best - scores[i]);
            if (gap > GapCap) gap = GapCap;
            var zi = -gap / t + Prior(feats[i], elo);
            z[i] = zi;
            if (zi > max) max = zi;
        }

        var sum = 0.0;
        for (var i = 0; i < n; i++) sum += Math.Exp(z[i] - max);
        var roll = rng.NextDouble() * sum;
        for (var i = 0; i < n; i++)
        {
            roll -= Math.Exp(z[i] - max);
            if (roll < 0)
                return i;
        }
        return n - 1;
    }

    private static double Prior(int feat, double elo)
    {
        var p = 0.0;
        if ((feat & 1) != 0) p += WCapture + WCaptureElo * elo;   // capture
        if ((feat & 2) != 0) p += WCastle;                        // castle
        if ((feat & 4) != 0) p += WPromo;                         // promotion
        if ((feat & 8) != 0) p += WCheck;                         // gives check
        p += ((feat >> 4) & 7) switch
        {
            0 => WPawn, 1 => WKnight, 2 => WBishop, 3 => WRook, 4 => WQueen, _ => 0.0,
        };
        return p;
    }

    public static void Reset()
    {
        Enabled = false;
        Elo = 0;
        RankDepth = 10;
    }
}
