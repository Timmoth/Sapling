using FluentAssertions;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;

namespace Sapling.Engine.Tests;

/// <summary>
/// a castling right must die with its rook no matter how the rook died, and a right that
/// outlives its rook (lenient FEN) must never produce a castle.
/// </summary>
public class CastlingRightsTests
{
    private const string UnderPromotionFen = "4k2r/6P1/8/8/8/8/8/4K3 w KQkq - 0 1";

    private const CastleRights AllRights =
        CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide | CastleRights.BlackKingSide |
        CastleRights.BlackQueenSide;

    [Theory]
    [InlineData("g7h8b")]
    [InlineData("g7h8n")]
    [InlineData("g7h8q")]
    [InlineData("g7h8r")]
    public void PromotionCapture_ClearsCapturedRooksRight(string uciMove)
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen(UnderPromotionFen));
        state.Board.CastleRights.Should().Be(AllRights);

        var move = Assert.Single(state.LegalMoves.Where(m => m.ToUciMoveName() == uciMove));

        state.Apply(move);

        (state.Board.CastleRights & CastleRights.BlackKingSide).Should().Be(CastleRights.None,
            $"{uciMove} captured the h8 rook");
        state.LegalMoves.Select(m => m.ToUciMoveName()).Should().NotContain("e8g8");
    }

    [Theory]
    [InlineData("b2a1b", true)]
    [InlineData("b2a1n", true)]
    [InlineData("b2a1q", false)]
    [InlineData("b2a1r", false)]
    public void BlackPromotionCapture_ClearsCapturedRooksRight(string uciMove, bool kingSafe)
    {
        using var state = new GameState(
            BoardStateExtensions.CreateBoardFromFen("4k3/8/8/8/8/8/1p6/R3K2R b KQ - 0 1"));
        state.Board.CastleRights.Should().Be(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide);

        var move = Assert.Single(state.LegalMoves.Where(m => m.ToUciMoveName() == uciMove));

        state.Apply(move);

        (state.Board.CastleRights & CastleRights.WhiteQueenSide).Should().Be(CastleRights.None,
            $"{uciMove} captured the a1 rook");
        var legal = state.LegalMoves.Select(m => m.ToUciMoveName());
        legal.Should().NotContain("e1c1");
        if (kingSafe)
        {
            legal.Should().Contain("e1g1", $"{uciMove} does not touch the first rank: castling stays legal");
        }
        else
        {
            legal.Should().NotContain("e1g1",
                $"{uciMove} gives check along the first rank: castling is illegal while in check");
        }
    }

    [Fact]
    public void PieceCapture_ClearsCapturedRooksRight()
    {
        using var state = new GameState(BoardStateExtensions.CreateBoardFromFen("r3k2r/8/1N6/8/8/8/8/R3K2R w KQkq - 0 1"));
        var move = Assert.Single(state.LegalMoves.Where(m => m.ToUciMoveName() == "b6a8"));

        state.Apply(move);

        (state.Board.CastleRights & CastleRights.BlackQueenSide).Should().Be(CastleRights.None);
        state.LegalMoves.Select(m => m.ToUciMoveName()).Should().NotContain("e8c8").And.Contain("e8g8");
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4K3 b k - 0 1", "e8g8")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 b q - 0 1", "e8c8")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w K - 0 1", "e1g1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w Q - 0 1", "e1c1")]
    public void CastleRequiresARookOnItsSquare(string fen, string castleMove)
    {
        var board = BoardStateExtensions.CreateBoardFromFen(fen);

        var moves = new List<uint>();
        board.GenerateLegalMoves(moves, false);

        moves.Select(m => m.ToUciMoveName()).Should().NotContain(castleMove);
        moves.Should().HaveCount(5, "only the five king moves are available");
    }

    [Fact]
    public void CastlingDensePosition_MatchesOracleAtDepthFour()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(
            "r3k2r/1bp2pP1/5n2/1P1Q4/1pPq4/5N2/1B1P2p1/R1R1K2R b KQkq c3 0 1");

        var nodes = board.PerftRootSequential(4, false).Aggregate(0UL, (sum, row) => sum + row.nodes);

        nodes.Should().Be(5_064_499);
    }

    [Fact]
    public void RootDivide_LabelsPromotionsWithTheirPromotionPiece()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(UnderPromotionFen);

        var divide = board.PerftRootSequential(1, false).Select(row => row.move).ToList();

        divide.Should().Contain(["g7g8q", "g7g8r", "g7g8b", "g7g8n"]);
        divide.Should().OnlyHaveUniqueItems();
    }
}
