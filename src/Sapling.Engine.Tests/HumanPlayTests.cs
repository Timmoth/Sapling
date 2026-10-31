using System.Text.Json;
using Sapling.Engine;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Tests;

public class HumanPlayTests
{
    [Fact]
    public void EmbeddedPolicy_MatchesPyTorchGoldenMoveDistributions()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "human_policy_inference_golden.json")));
        Assert.Equal(1, fixture.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal(fixture.RootElement.GetProperty("model_sha256").GetString(), HumanMovePolicy.ModelSha256);

        foreach (var testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var board = BoardStateExtensions.CreateBoardFromFen(testCase.GetProperty("fen").GetString()!);
            var boardFeatures = HumanPolicyEncoding.EncodeBoard(ref board);
            var legalMoves = new List<uint>();
            board.GenerateLegalMoves(legalMoves, captureOnly: false);
            var scoresByMove = testCase.GetProperty("engine_scores");
            var bestScore = scoresByMove.EnumerateObject().Max(x => x.Value.GetInt32());
            var moveFeatures = new float[legalMoves.Count * HumanPolicyEncoding.MoveFeatures];
            for (var i = 0; i < legalMoves.Count; i++)
            {
                var uci = legalMoves[i].ToUciMoveName();
                var score = scoresByMove.GetProperty(uci).GetInt32();
                HumanPolicyEncoding.EncodeMove(legalMoves[i], score, bestScore,
                    moveFeatures.AsSpan(i * HumanPolicyEncoding.MoveFeatures, HumanPolicyEncoding.MoveFeatures));
            }

            var actual = HumanMovePolicy.ScoreCandidates(boardFeatures, moveFeatures, legalMoves.Count,
                testCase.GetProperty("rating").GetInt32());
            var expectedByMove = testCase.GetProperty("expected_logits");
            var expected = new float[legalMoves.Count];
            for (var i = 0; i < legalMoves.Count; i++)
                expected[i] = expectedByMove.GetProperty(legalMoves[i].ToUciMoveName()).GetSingle();

            Assert.NotNull(Softmax(actual));
            var actualProbability = Softmax(actual);
            var expectedProbability = Softmax(expected);
            Assert.True(actualProbability.Zip(expectedProbability).Sum(pair => Math.Abs(pair.First - pair.Second)) <= 0.002f, testCase.GetProperty("name").GetString());
            Assert.True(Array.IndexOf(actual, actual.Max()) == Array.IndexOf(expected, expected.Max()), testCase.GetProperty("name").GetString());
        }
    }

    [Fact]
    public void EmbeddedPolicy_MultiRatingScoringMatchesSingleRatingScoring()
    {
        using var state = GameState.InitialState();
        var board = state.Board;
        var boardFeatures = HumanPolicyEncoding.EncodeBoard(ref board);
        var legalMoves = new List<uint>();
        board.GenerateLegalMoves(legalMoves, captureOnly: false);
        var moveFeatures = new float[legalMoves.Count * HumanPolicyEncoding.MoveFeatures];
        var ratings = new[] { 500, 800, 1500, 2200 };
        var multiRatingLogits = HumanMovePolicy.ScoreCandidatesForRatings(
            boardFeatures, moveFeatures, legalMoves.Count, ratings);

        Assert.Equal(ratings.Length, multiRatingLogits.Length);
        for (var i = 0; i < ratings.Length; i++)
            Assert.Equal(HumanMovePolicy.ScoreCandidates(boardFeatures, moveFeatures,
                legalMoves.Count, ratings[i]), multiRatingLogits[i]);
        Assert.Throws<ArgumentException>(() => HumanMovePolicy.ScoreCandidatesForRatings(
            boardFeatures, moveFeatures, legalMoves.Count, Array.Empty<int>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => HumanMovePolicy.ScoreCandidatesForRatings(
            boardFeatures, moveFeatures, legalMoves.Count, new[] { 499 }));
    }

    [Fact]
    public void HumanPlayCalibration_AppliesStaticDepthEightHumanDataCalibration()
    {
        Assert.True(HumanPlayCalibration.AppliesToCurrentModel);
        Assert.Equal(new HumanPlaySettings(500, 1f, .5f), HumanPlayCalibration.ForDisplayElo(500));
        Assert.Equal(new HumanPlaySettings(875, 1f, .6875f), HumanPlayCalibration.ForDisplayElo(800));
        Assert.Equal(new HumanPlaySettings(1000, 1f, .75f), HumanPlayCalibration.ForDisplayElo(900));
        Assert.Equal(new HumanPlaySettings(1088, 1f, .75f), HumanPlayCalibration.ForDisplayElo(1000));
        Assert.Equal(new HumanPlaySettings(1500, 1f, .625f), HumanPlayCalibration.ForDisplayElo(1500));
        Assert.Equal(new HumanPlaySettings(1700, 1f, .375f), HumanPlayCalibration.ForDisplayElo(1700));
        Assert.Equal(new HumanPlaySettings(1750, 1.0125f, .5625f), HumanPlayCalibration.ForDisplayElo(1800));
        Assert.Equal(new HumanPlaySettings(2050, 1.05f, .75f), HumanPlayCalibration.ForDisplayElo(2200));
        Assert.Equal(new HumanPlaySettings(0, 1f, 0f), HumanPlayCalibration.ForDisplayElo(0));
    }

    [Theory]
    [InlineData("6rk/8/8/8/8/8/4R3/K7 w - - 0 1", "e2g2", true)]
    [InlineData("6rk/8/8/8/8/8/4R3/5K2 w - - 0 1", "e2g2", false)]
    public void DirectCaptureRisk_RequiresLegalCaptureWithoutImmediateRecapture(string fen, string uci, bool expected)
    {
        var board = BoardStateExtensions.CreateBoardFromFen(fen);
        var originalHash = board.Hash;
        var legalMoves = new List<uint>();
        board.GenerateLegalMoves(legalMoves, captureOnly: false);
        var move = Assert.Single(legalMoves, candidate => candidate.ToUciMoveName() == uci);

        Assert.Equal(expected, DirectCaptureRisk.IsRiskAfterMove(ref board, move));
        Assert.Equal(originalHash, board.Hash);
    }

    [Fact]
    public void DisabledHumanPlay_LeavesEngineBestMoveSelected()
    {
        HumanPlay.Reset();
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);
        var ranked = searcher.RankRootMoves(state, 2);
        var board = state.Board;

        Assert.Equal(0, HumanPlay.PickMove(new Random(1), ref board, ranked, out var probability));
        Assert.Equal(1f, probability);
    }

    [Fact]
    public void SelectMove_PreservesThePolicyChoiceWhenItIsRankZero()
    {
        HumanPlay.Reset();
        HumanPlay.SetElo(1500);
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);
        var ranked = searcher.RankRootMoves(state, 2);
        var board = state.Board;

        var selection = HumanPlay.SelectMove(new ZeroRandom(), ref board, ranked);

        Assert.Equal(0, selection.Rank);
        Assert.Equal(ranked[0].Move, selection.Move);
        Assert.True(selection.Probability > 0f);
        HumanPlay.Reset();
    }

    [Fact]
    public void EmbeddedPolicySampler_AlwaysSelectsARankedLegalMove()
    {
        HumanPlay.Reset();
        HumanPlay.SetElo(1500);
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);
        var ranked = searcher.RankRootMoves(state, 2);
        var board = state.Board;
        var rng = new Random(42);

        for (var i = 0; i < 100; i++)
        {
            var selected = HumanPlay.PickMove(rng, ref board, ranked, out var probability);
            Assert.InRange(selected, 0, ranked.Count - 1);
            Assert.Contains(ranked[selected].Move, state.LegalMoves);
            Assert.True(probability > 0f);
            Assert.True(probability <= 1f);
        }
    }

    [Fact]
    public void EmbeddedPolicy_UsesEngineScoresInCandidateFeatures()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(Constants.InitialState);
        var boardFeatures = HumanPolicyEncoding.EncodeBoard(ref board);
        var legalMoves = new List<uint>();
        board.GenerateLegalMoves(legalMoves, captureOnly: false);
        var neutralMoves = new float[legalMoves.Count * HumanPolicyEncoding.MoveFeatures];
        var scoredMoves = new float[neutralMoves.Length];
        for (var i = 0; i < legalMoves.Count; i++)
        {
            HumanPolicyEncoding.EncodeMove(legalMoves[i], 0, 0,
                neutralMoves.AsSpan(i * HumanPolicyEncoding.MoveFeatures, HumanPolicyEncoding.MoveFeatures));
            HumanPolicyEncoding.EncodeMove(legalMoves[i], i == 0 ? -1000 : 0, 0,
                scoredMoves.AsSpan(i * HumanPolicyEncoding.MoveFeatures, HumanPolicyEncoding.MoveFeatures));
        }

        var neutralLogits = HumanMovePolicy.ScoreCandidates(boardFeatures, neutralMoves, legalMoves.Count, 1500);
        var scoredLogits = HumanMovePolicy.ScoreCandidates(boardFeatures, scoredMoves, legalMoves.Count, 1500);

        Assert.True(Softmax(neutralLogits).Zip(Softmax(scoredLogits))
            .Sum(pair => Math.Abs(pair.First - pair.Second)) > 1e-6f);
    }

    [Fact]
    public void EmbeddedPolicy_UsesRatingConditioning()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(Constants.InitialState);
        var boardFeatures = HumanPolicyEncoding.EncodeBoard(ref board);
        var legalMoves = new List<uint>();
        board.GenerateLegalMoves(legalMoves, captureOnly: false);
        var moveFeatures = new float[legalMoves.Count * HumanPolicyEncoding.MoveFeatures];
        for (var i = 0; i < legalMoves.Count; i++)
            HumanPolicyEncoding.EncodeMove(legalMoves[i], i * 10, (legalMoves.Count - 1) * 10,
                moveFeatures.AsSpan(i * HumanPolicyEncoding.MoveFeatures, HumanPolicyEncoding.MoveFeatures));

        var lowRating = HumanMovePolicy.ScoreCandidates(boardFeatures, moveFeatures, legalMoves.Count, 800);
        var highRating = HumanMovePolicy.ScoreCandidates(boardFeatures, moveFeatures, legalMoves.Count, 2200);

        Assert.True(Softmax(lowRating).Zip(Softmax(highRating))
            .Sum(pair => Math.Abs(pair.First - pair.Second)) > 1e-6f);
    }

    [Fact]
    public void PolicyProbabilities_TemperatureChangesEntropyAndStayNormalized()
    {
        var logits = new float[] { 2f, 1f, -1f };
        var cool = HumanMovePolicy.Probabilities(logits, 0.5f);
        var baseLine = HumanMovePolicy.Probabilities(logits);
        var warm = HumanMovePolicy.Probabilities(logits, 2f);

        Assert.InRange(cool.Sum(), 0.99999f, 1.00001f);
        Assert.InRange(baseLine.Sum(), 0.99999f, 1.00001f);
        Assert.InRange(warm.Sum(), 0.99999f, 1.00001f);
        Assert.True(cool[0] > baseLine[0]);
        Assert.True(baseLine[0] > warm[0]);
    }

    [Fact]
    public void PolicySampler_SoftPenaltyReducesMarkedCandidateProbability()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(Constants.InitialState);
        var boardFeatures = HumanPolicyEncoding.EncodeBoard(ref board);
        var moves = new List<uint>();
        board.GenerateLegalMoves(moves, captureOnly: false);
        var moveFeatures = new float[moves.Count * HumanPolicyEncoding.MoveFeatures];
        var penaltyMask = new bool[moves.Count];
        penaltyMask[0] = true;

        var baselineIndex = HumanMovePolicy.Sample(new ZeroRandom(), boardFeatures, moveFeatures,
            moves.Count, 1500, 1f, out var baselineProbability);
        var penalizedIndex = HumanMovePolicy.Sample(new ZeroRandom(), boardFeatures, moveFeatures,
            moves.Count, 1500, 1f, out var penalizedProbability, penaltyMask, 1f);

        Assert.Equal(0, baselineIndex);
        Assert.Equal(0, penalizedIndex);
        Assert.True(penalizedProbability < baselineProbability);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void PolicyProbabilities_RejectInvalidTemperature(float temperature)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HumanMovePolicy.Probabilities(new float[] { 1f, 0f }, temperature));
    }

    [Fact]
    public void HumanPlay_ResetRestoresDefaultTemperature()
    {
        HumanPlay.SetTemperature(1.75f);
        Assert.Equal(1.75f, HumanPlay.Temperature);

        HumanPlay.Reset();

        Assert.Equal(1f, HumanPlay.Temperature);
    }

    [Fact]
    public void EmbeddedPolicySampler_IsSeededAndReturnsSelectedSoftmaxProbability()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(Constants.InitialState);
        var boardFeatures = HumanPolicyEncoding.EncodeBoard(ref board);
        var legalMoves = new List<uint>();
        board.GenerateLegalMoves(legalMoves, captureOnly: false);
        var moveFeatures = new float[legalMoves.Count * HumanPolicyEncoding.MoveFeatures];
        for (var i = 0; i < legalMoves.Count; i++)
            HumanPolicyEncoding.EncodeMove(legalMoves[i], i * 10, (legalMoves.Count - 1) * 10,
                moveFeatures.AsSpan(i * HumanPolicyEncoding.MoveFeatures, HumanPolicyEncoding.MoveFeatures));
        var expectedProbabilities = Softmax(HumanMovePolicy.ScoreCandidates(
            boardFeatures, moveFeatures, legalMoves.Count, 1500));

        var selected = HumanMovePolicy.Sample(new Random(8128), boardFeatures, moveFeatures,
            legalMoves.Count, 1500, out var probability);
        var repeated = HumanMovePolicy.Sample(new Random(8128), boardFeatures, moveFeatures,
            legalMoves.Count, 1500, out var repeatedProbability);

        Assert.Equal(repeated, selected);
        Assert.Equal(repeatedProbability, probability, 1e-7f);
        Assert.Equal(expectedProbabilities[selected], probability, 1e-6f);

        const float temperature = 1.6f;
        var temperatureProbabilities = HumanMovePolicy.Probabilities(
            HumanMovePolicy.ScoreCandidates(boardFeatures, moveFeatures, legalMoves.Count, 1500), temperature);
        var temperatureSelected = HumanMovePolicy.Sample(new Random(8128), boardFeatures, moveFeatures,
            legalMoves.Count, 1500, temperature, out var temperatureProbability);
        Assert.Equal(temperatureProbabilities[temperatureSelected], temperatureProbability, 1e-6f);
    }

    [Fact]
    public void EmbeddedPolicy_RejectsMalformedTensorShapesAndOutOfRangeRatings()
    {
        var board = new float[HumanPolicyEncoding.BoardChannels * HumanPolicyEncoding.SquaresPerBoard];
        var moves = new float[HumanPolicyEncoding.MoveFeatures];

        var shortBoard = () => HumanMovePolicy.ScoreCandidates(board.AsSpan(1), moves, 1, 1500);
        var wrongMoveCount = () => HumanMovePolicy.ScoreCandidates(board, moves, 2, 1500);
        var invalidRating = () => HumanMovePolicy.ScoreCandidates(board, moves, 1, 499);

        Assert.Throws<ArgumentException>(shortBoard);
        Assert.Throws<ArgumentException>(wrongMoveCount);
        Assert.Throws<ArgumentOutOfRangeException>(invalidRating);
    }

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 500)]
    [InlineData(300, 500)]
    [InlineData(1500, 1500)]
    [InlineData(3000, 2200)]
    public void HumanElo_MapsToTheTrainedPolicyRange(int requested, int expected)
    {
        HumanPlay.SetElo(requested);
        Assert.Equal(expected, HumanPlay.Elo);
        Assert.Equal(expected != 0, HumanPlay.Enabled);
        HumanPlay.Reset();
    }

    [Fact]
    public void RankRootMoves_BestFirst_MateFirst_WithFlags()
    {
        HumanPlay.Reset();
        using var state = GameState.InitialState();
        state.ResetToFen("6k1/5ppp/8/8/8/8/8/R6K w - - 0 1"); // Ra8#
        using var searcher = new ParallelSearcher(1024);

        var ranked = searcher.RankRootMoves(state, 2);

        Assert.NotEmpty(ranked);
        Assert.Equal(state.LegalMoves.Count, ranked.Count);
        Assert.True(ranked[0].Score > 1000);
        Assert.Contains(ranked[0].Move, state.LegalMoves);
        for (var i = 1; i < ranked.Count; i++)
            Assert.True(ranked[i - 1].Score >= ranked[i].Score);
    }

    private static float[] Softmax(float[] logits)
    {
        var maximum = logits.Max();
        var values = logits.Select(value => Math.Exp(value - maximum)).ToArray();
        var sum = values.Sum();
        return values.Select(value => (float)(value / sum)).ToArray();
    }

    private sealed class ZeroRandom : Random
    {
        public override double NextDouble() => 0d;
    }
}
