using FluentAssertions;
using Sapling.Engine;
using Sapling.Engine.DataGen;

namespace Sapling.Engine.Tests;

public class DataGeneratorBehaviorTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(DataGenerator.RandomMoves - 1, true)]
    [InlineData(DataGenerator.RandomMoves, false)]
    [InlineData(DataGenerator.RandomMoves + 1, false)]
    public void RandomOpening_UsesExactlyTheConfiguredNumberOfPlies(int randomMoveCount, bool expected)
    {
        DataGenerator.IsRandomOpeningPly(randomMoveCount).Should().Be(expected);
    }

    [Fact]
    public void InitialBoard_UsesChess960StartingPositionWhenConfigured()
    {
        var board = DataGenerator.CreateInitialBoard(useChess960: true, chess960Index: 0);

        board.Is960.Should().BeTrue();
        board.WhiteToMove.Should().BeTrue();
        board.CastleRights.Should().NotBe(0);
    }

    [Fact]
    public void InitialBoard_UsesStandardPositionWhenChess960IsDisabled()
    {
        var board = DataGenerator.CreateInitialBoard(useChess960: false);

        board.Is960.Should().BeFalse();
        board.WhiteToMove.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, Viriformat.WdlDraw)]
    [InlineData(1, Viriformat.WdlBlackWin)]
    [InlineData(2, Viriformat.WdlWhiteWin)]
    public void GameOutcome_MapsToViriformatWdl(byte outcome, byte expectedWdl)
    {
        DataGenerator.ToViriformatWdl(outcome).Should().Be(expectedWdl);
    }

    [Fact]
    public void GameOutcome_RejectsUnknownValuesInsteadOfRecordingAWhiteWin()
    {
        var act = () => DataGenerator.ToViriformatWdl(3);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(true, 2500, 1)]
    [InlineData(true, -2500, 2)]
    [InlineData(false, 2500, 2)]
    [InlineData(false, -2500, 1)]
    public void Adjudication_MapsPreMoveSideToMoveScoreToTheCorrectWinner(
        bool whiteToMoveAfterMove, int score, byte expectedOutcome)
    {
        DataGenerator.GetAdjudicatedOutcome(whiteToMoveAfterMove, score).Should().Be(expectedOutcome);
    }

    [Theory]
    [InlineData("7k/6Q1/6K1/8/8/8/8/8 b - - 0 1", 2)] // black is checkmated: white wins
    [InlineData("8/8/8/8/8/1k6/1q6/K7 w - - 0 1", 1)] // white is checkmated: black wins
    [InlineData("7k/8/8/8/8/8/8/K7 w - - 0 1", 0)] // dead position draw
    public void GameStateOutcome_UsesAbsoluteWinnerColor(string fen, byte expectedOutcome)
    {
        using var game = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));

        game.WinDrawLoose().Should().Be(expectedOutcome);
    }

    [Fact]
    public void MirrorFen_IsAnInvolutionForAllChess960Starts()
    {
        foreach (var fen in Chess960.Fens)
        {
            DataGenerator.MirrorFen(DataGenerator.MirrorFen(fen)).Should().Be(fen);
        }
    }

    [Fact]
    public void MirrorFen_SwapsColoursRanksSideToMoveAndCastlingCase()
    {
        const string fen = "rbnkqbnr/pppppppp/8/8/8/8/PPPPPPPP/RBNKQBNR w HBhb - 0 1";

        var mirrored = DataGenerator.MirrorFen(fen);

        mirrored.Should().Be("rbnkqbnr/pppppppp/8/8/8/8/PPPPPPPP/RBNKQBNR b BHbh - 0 1");
    }

    [Fact]
    public void MirrorFen_ProducesParsableBoardWithBlackToMoveForAllChess960Starts()
    {
        foreach (var fen in Chess960.Fens)
        {
            var mirrored = DataGenerator.MirrorFen(fen);
            using var game = new GameState(BoardStateExtensions.CreateBoardFromFen(mirrored));

            game.Board.WhiteToMove.Should().BeFalse();
            game.Board.CastleRights.Should().NotBe(0);
            game.LegalMoves.Count.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void MirrorFen_KeepsStandardStartPlacementAndFlipsSideToMove()
    {
        var mirrored = DataGenerator.MirrorFen(Constants.InitialState);

        mirrored.Should().Be("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1");
    }
}
