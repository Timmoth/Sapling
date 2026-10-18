using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tuning;

/// <summary>Rating-conditioned move selection backed by the embedded trained human policy.</summary>
public static class HumanPlay
{
    public const int MinElo = HumanPolicyEncoding.MinRating;
    public const int MaxElo = HumanPolicyEncoding.MaxRating;
    public const int DefaultRankDepth = 8;

    public static bool Enabled { get; private set; }
    public static int Elo { get; private set; }
    public static int RankDepth { get; set; } = DefaultRankDepth;

    /// <summary>Zero disables human play. Active ratings are clamped to the trained range.</summary>
    public static void SetElo(int elo)
    {
        Elo = elo <= 0 ? 0 : Math.Clamp(elo, MinElo, MaxElo);
        Enabled = Elo != 0;
    }

    /// <summary>Samples among exact, best-first root candidates using schema-v1 policy features.</summary>
    public static int PickMove(Random rng, ref BoardStateData board,
        IReadOnlyList<(uint Move, int Score, int Feat)> ranked, out float selectedProbability)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(ranked);
        selectedProbability = 1f;
        if (!Enabled || ranked.Count <= 1)
            return 0;

        var boardFeatures = HumanPolicyEncoding.EncodeBoard(ref board);
        var moveFeatures = new float[checked(ranked.Count * HumanPolicyEncoding.MoveFeatures)];
        var bestScore = ranked.Max(candidate => candidate.Score);
        for (var i = 0; i < ranked.Count; i++)
        {
            HumanPolicyEncoding.EncodeMove(ranked[i].Move, ranked[i].Score, bestScore,
                moveFeatures.AsSpan(i * HumanPolicyEncoding.MoveFeatures, HumanPolicyEncoding.MoveFeatures));
        }
        return HumanMovePolicy.Sample(rng, boardFeatures, moveFeatures, ranked.Count, Elo,
            out selectedProbability);
    }

    public static void Reset()
    {
        Enabled = false;
        Elo = 0;
        RankDepth = DefaultRankDepth;
    }
}
