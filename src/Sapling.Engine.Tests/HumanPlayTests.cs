using System.Text.Json;
using FluentAssertions;
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
        fixture.RootElement.GetProperty("schema_version").GetInt32().Should().Be(1);

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

            Softmax(actual).Should().NotBeNull();
            var actualProbability = Softmax(actual);
            var expectedProbability = Softmax(expected);
            actualProbability.Zip(expectedProbability).Sum(pair => Math.Abs(pair.First - pair.Second))
                .Should().BeLessThanOrEqualTo(0.002f, testCase.GetProperty("name").GetString());
            Array.IndexOf(actual, actual.Max()).Should().Be(Array.IndexOf(expected, expected.Max()),
                testCase.GetProperty("name").GetString());
        }
    }

    [Fact]
    public void DisabledHumanPlay_LeavesEngineBestMoveSelected()
    {
        HumanPlay.Reset();
        using var state = GameState.InitialState();
        using var searcher = new ParallelSearcher(1024);
        var ranked = searcher.RankRootMoves(state, 2);
        var board = state.Board;

        HumanPlay.PickMove(new Random(1), ref board, ranked, out var probability).Should().Be(0);
        probability.Should().Be(1f);
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
            selected.Should().BeInRange(0, ranked.Count - 1);
            state.LegalMoves.Should().Contain(ranked[selected].Move);
            probability.Should().BeGreaterThan(0f).And.BeLessThanOrEqualTo(1f);
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

        Softmax(neutralLogits).Zip(Softmax(scoredLogits))
            .Sum(pair => Math.Abs(pair.First - pair.Second)).Should().BeGreaterThan(1e-6f);
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

        Softmax(lowRating).Zip(Softmax(highRating))
            .Sum(pair => Math.Abs(pair.First - pair.Second)).Should().BeGreaterThan(1e-6f);
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

        selected.Should().Be(repeated);
        probability.Should().BeApproximately(repeatedProbability, 1e-7f);
        probability.Should().BeApproximately(expectedProbabilities[selected], 1e-6f);
    }

    [Fact]
    public void EmbeddedPolicy_RejectsMalformedTensorShapesAndOutOfRangeRatings()
    {
        var board = new float[HumanPolicyEncoding.BoardChannels * HumanPolicyEncoding.SquaresPerBoard];
        var moves = new float[HumanPolicyEncoding.MoveFeatures];

        var shortBoard = () => HumanMovePolicy.ScoreCandidates(board.AsSpan(1), moves, 1, 1500);
        var wrongMoveCount = () => HumanMovePolicy.ScoreCandidates(board, moves, 2, 1500);
        var invalidRating = () => HumanMovePolicy.ScoreCandidates(board, moves, 1, 799);

        shortBoard.Should().Throw<ArgumentException>();
        wrongMoveCount.Should().Throw<ArgumentException>();
        invalidRating.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 800)]
    [InlineData(300, 800)]
    [InlineData(1500, 1500)]
    [InlineData(3000, 2200)]
    public void HumanElo_MapsToTheTrainedPolicyRange(int requested, int expected)
    {
        HumanPlay.SetElo(requested);
        HumanPlay.Elo.Should().Be(expected);
        HumanPlay.Enabled.Should().Be(expected != 0);
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

        ranked.Should().NotBeEmpty();
        ranked.Count.Should().Be(state.LegalMoves.Count);
        ranked[0].Score.Should().BeGreaterThan(1000);
        state.LegalMoves.Should().Contain(ranked[0].Move);
        for (var i = 1; i < ranked.Count; i++)
            ranked[i - 1].Score.Should().BeGreaterThanOrEqualTo(ranked[i].Score);
    }

    private static float[] Softmax(float[] logits)
    {
        var maximum = logits.Max();
        var values = logits.Select(value => Math.Exp(value - maximum)).ToArray();
        var sum = values.Sum();
        return values.Select(value => (float)(value / sum)).ToArray();
    }
}
