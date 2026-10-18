using System.Text.Json;
using FluentAssertions;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

public class HumanPolicyEncodingTests
{
    [Fact]
    public void Encoders_MatchSharedGoldenFixtures()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "human_policy_golden_cases.json")));
        json.RootElement.GetProperty("schema_version").GetInt32().Should().Be(HumanPolicyEncoding.SchemaVersion);

        foreach (var testCase in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            var fen = testCase.GetProperty("fen").GetString()!;
            var board = BoardStateExtensions.CreateBoardFromFen(fen);
            var encodedBoard = HumanPolicyEncoding.EncodeBoard(ref board);
            var expectedBoard = new HashSet<int>();

            foreach (var channel in testCase.GetProperty("piece_squares").EnumerateObject())
            foreach (var square in channel.Value.EnumerateArray())
                expectedBoard.Add(int.Parse(channel.Name) * 64 + square.GetInt32());

            foreach (var channel in testCase.GetProperty("full_planes").EnumerateArray())
            {
                var start = channel.GetInt32() * 64;
                for (var square = 0; square < 64; square++) expectedBoard.Add(start + square);
            }

            if (testCase.GetProperty("ep_square").ValueKind != JsonValueKind.Null)
                expectedBoard.Add(17 * 64 + testCase.GetProperty("ep_square").GetInt32());

            var activeBoard = Enumerable.Range(0, encodedBoard.Length)
                .Where(i => encodedBoard[i] != 0f).ToHashSet();
            activeBoard.Should().BeEquivalentTo(expectedBoard, testCase.GetProperty("name").GetString());

            var legalMoves = new List<uint>();
            board.GenerateLegalMoves(legalMoves, captureOnly: false);
            foreach (var moveCase in testCase.GetProperty("moves").EnumerateObject())
            {
                var move = legalMoves.Single(m => m.ToUciMoveName() == moveCase.Name);
                var encodedMove = HumanPolicyEncoding.EncodeMove(move, 0, 0);
                var activeMove = Enumerable.Range(0, 132)
                    .Where(i => encodedMove[i] != 0f).ToArray();
                activeMove.Should().Equal(moveCase.Value.EnumerateArray().Select(x => x.GetInt32()),
                    $"move {moveCase.Name} in {testCase.GetProperty("name").GetString()}");
            }
        }

        foreach (var featureCase in json.RootElement.GetProperty("engine_feature_cases").EnumerateArray())
        {
            var board = BoardStateExtensions.CreateBoardFromFen(featureCase.GetProperty("fen").GetString()!);
            var legalMoves = new List<uint>();
            board.GenerateLegalMoves(legalMoves, captureOnly: false);
            var uci = featureCase.GetProperty("move").GetString();
            var move = legalMoves.Single(m => m.ToUciMoveName() == uci);
            var encoded = HumanPolicyEncoding.EncodeMove(
                move,
                featureCase.GetProperty("root_score").GetInt32(),
                featureCase.GetProperty("best_root_score").GetInt32());
            var expected = featureCase.GetProperty("expected").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            encoded[132].Should().BeApproximately((float)expected[0], 1e-7f);
            encoded[133].Should().BeApproximately((float)expected[1], 1e-7f);
        }
    }

    [Theory]
    [InlineData(800, -1f)]
    [InlineData(1500, 0f)]
    [InlineData(2200, 1f)]
    public void RatingEncoder_UsesSpecifiedScale(int rating, float expected)
    {
        HumanPolicyEncoding.EncodeRating(rating).Should().Be(expected);
    }

    [Theory]
    [InlineData(799)]
    [InlineData(2201)]
    public void RatingEncoder_RejectsOutOfRangeValues(int rating)
    {
        var act = () => HumanPolicyEncoding.EncodeRating(rating);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
