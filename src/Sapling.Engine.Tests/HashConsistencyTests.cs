using FluentAssertions;

namespace Sapling.Engine.Tests;

/// <summary>
/// Locks the incremental Zobrist hash against an independent full recompute. The search, TT and
/// repetition detection all key off Board.Hash, which is updated incrementally on every Apply.
/// A refactor that mishandles a castling/ep/promotion/capture hash delta would silently corrupt
/// TT lookups and repetition detection, so every ply must match CalculateZobristKey exactly.
/// </summary>
public class HashConsistencyTests
{
    private static GameState StateFromFen(string fen) =>
        new(BoardStateExtensions.CreateBoardFromFen(fen));

    private static uint Move(GameState state, string uci) =>
        state.LegalMoves.First(m => m.ToUciMoveName() == uci);

    private static void AssertHashMatchesRecompute(GameState state, string context)
    {
        var recomputed = Zobrist.CalculateZobristKey(ref state.Board);
        state.Board.Hash.Should().Be(recomputed, context);
    }

    [Fact]
    public void InitialPosition_HashIsSelfConsistentAndPinned()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(Constants.InitialState);
        board.Hash.Should().Be(Zobrist.CalculateZobristKey(ref board));
        // Pins the Zobrist table itself: a changed key set changes this value.
        board.Hash.Should().Be(5060803636482931868UL);
    }

    [Theory]
    // Each sequence exercises a distinct incremental-hash delta path.
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1", new[] { "e1g1" })]           // castling (king+rook+rights)
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1", new[] { "e1c1" })]           // queenside castle
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", new[] { "e5d6" })]          // en-passant capture + ep square clear
    [InlineData("4k3/4p3/8/3P4/8/8/8/4K3 b - - 0 1", new[] { "e7e5" })]         // double push with a capturable ep target
    [InlineData("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", new[] { "a7a8q" })]            // promotion
    [InlineData("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", new[] { "a7a8n" })]            // under-promotion
    [InlineData("4k3/8/8/8/8/8/4r3/4K3 w - - 0 1", new[] { "e1e2" })]            // king capture
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        new[] { "e2e4", "e7e5", "g1f3", "b8c6", "f1c4", "g8f6", "d2d3", "f8c5" })] // quiet opening mix
    public void IncrementalHash_MatchesRecompute_AfterSpecificSequences(string fen, string[] moves)
    {
        using var state = StateFromFen(fen);
        AssertHashMatchesRecompute(state, "start position");

        foreach (var uci in moves)
        {
            state.LegalMoves.Should().Contain(m => m.ToUciMoveName() == uci, $"{uci} must be legal");
            state.Apply(Move(state, uci));
            AssertHashMatchesRecompute(state, $"after {uci}");
        }
    }

    [Fact]
    public void IncrementalHash_MatchesRecompute_ThroughoutRandomLegalPlay()
    {
        var rng = new Random(20261012);
        using var state = GameState.InitialState();

        for (var ply = 0; ply < 120 && !state.GameOver(); ply++)
        {
            var move = state.LegalMoves[rng.Next(state.LegalMoves.Count)];
            state.Apply(move);
            AssertHashMatchesRecompute(state, $"random ply {ply + 1}");
        }
    }

    [Fact]
    public void IdenticalPositions_ProduceIdenticalHashes()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 5 3";
        var a = BoardStateExtensions.CreateBoardFromFen(fen);
        var b = BoardStateExtensions.CreateBoardFromFen(fen);
        a.Hash.Should().Be(b.Hash);
    }
}
