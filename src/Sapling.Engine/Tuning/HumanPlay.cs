using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tuning;

public readonly record struct HumanMoveSelection(uint Move, int Rank, float Probability);

/// <summary>Rating-conditioned move selection backed by the embedded trained human policy.</summary>
public static class HumanPlay
{
    public const int MinElo = HumanPolicyEncoding.MinRating;
    public const int MaxElo = HumanPolicyEncoding.MaxRating;
    public const int DefaultRankDepth = 8;

    public static bool Enabled { get; private set; }
    public static int Elo { get; private set; }
    public static float Temperature { get; private set; } = 1f;
    public static int RankDepth { get; set; } = DefaultRankDepth;

    /// <summary>Zero disables human play. Active ratings are clamped to the trained range.</summary>
    public static void SetElo(int elo)
    {
        Elo = elo <= 0 ? 0 : Math.Clamp(elo, MinElo, MaxElo);
        Enabled = Elo != 0;
    }

    /// <summary>Sets the calibrated sampling temperature; 1 preserves the trained distribution.</summary>
    public static void SetTemperature(float temperature)
    {
        if (!float.IsFinite(temperature) || temperature <= 0f)
            throw new ArgumentOutOfRangeException(nameof(temperature), "Temperature must be finite and positive.");
        Temperature = temperature;
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
        var settings = HumanPlayCalibration.ForDisplayElo(Elo);
        if (settings.DirectCaptureRiskPenalty > 0f)
        {
            var directCaptureRisk = new bool[ranked.Count];
            var replyMoves = new List<uint>(32);
            var recaptureMoves = new List<uint>(32);
            for (var i = 0; i < ranked.Count; i++)
                directCaptureRisk[i] = DirectCaptureRisk.IsRiskAfterMove(ref board, ranked[i].Move,
                    replyMoves, recaptureMoves);
            return HumanMovePolicy.Sample(rng, boardFeatures, moveFeatures, ranked.Count,
                settings.ModelElo, Temperature * settings.Temperature,
                out selectedProbability, directCaptureRisk, settings.DirectCaptureRiskPenalty);
        }
        return HumanMovePolicy.Sample(rng, boardFeatures, moveFeatures, ranked.Count,
            settings.ModelElo, Temperature * settings.Temperature,
            out selectedProbability);
    }

    /// <summary>Returns the selected move identity, including when it is ranked first.</summary>
    public static HumanMoveSelection SelectMove(Random rng, ref BoardStateData board,
        IReadOnlyList<(uint Move, int Score, int Feat)> ranked)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(ranked);
        if (ranked.Count == 0)
            throw new ArgumentException("At least one legal move is required.", nameof(ranked));

        var rank = PickMove(rng, ref board, ranked, out var probability);
        return new HumanMoveSelection(ranked[rank].Move, rank, probability);
    }

    public static void Reset()
    {
        Enabled = false;
        Elo = 0;
        Temperature = 1f;
        RankDepth = DefaultRankDepth;
    }
}
