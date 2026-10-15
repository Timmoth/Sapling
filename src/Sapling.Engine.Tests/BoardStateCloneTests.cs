using FluentAssertions;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tests;

/// <summary>
/// The search and perft hot paths copy BoardStateData by value / CloneTo / CopyBlock. A refactor of
/// the explicit struct layout that drops or misaligns a field would corrupt a copy
/// silently. Clone must preserve the hash and the full legal-move set, including castling rights,
/// en-passant state and piece placement.
/// </summary>
public class BoardStateCloneTests
{
    private static List<string> LegalNames(ref BoardStateData board)
    {
        var moves = new List<uint>();
        board.GenerateLegalMoves(moves, false);
        return moves.Select(m => m.ToUciMoveName()).OrderBy(s => s).ToList();
    }

    [Theory]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    public void CloneTo_PreservesHashAndLegalMoves(string fen)
    {
        var source = BoardStateExtensions.CreateBoardFromFen(fen);
        var copy = default(BoardStateData);
        source.CloneTo(ref copy);

        copy.Hash.Should().Be(source.Hash);
        LegalNames(ref copy).Should().BeEquivalentTo(LegalNames(ref source));
    }

    [Fact]
    public void CloneTo_PreservesState_AfterIncrementalPlay()
    {
        var rng = new Random(20261012);
        using var state = GameState.InitialState();
        for (var ply = 0; ply < 30 && !state.GameOver(); ply++)
            state.Apply(state.LegalMoves[rng.Next(state.LegalMoves.Count)]);

        var copy = default(BoardStateData);
        state.Board.CloneTo(ref copy);

        copy.Hash.Should().Be(state.Board.Hash);
        copy.WhiteToMove.Should().Be(state.Board.WhiteToMove);
        copy.CastleRights.Should().Be(state.Board.CastleRights);
        copy.EnPassantFile.Should().Be(state.Board.EnPassantFile);
        LegalNames(ref copy).Should().BeEquivalentTo(LegalNames(ref state.Board));
    }
}
