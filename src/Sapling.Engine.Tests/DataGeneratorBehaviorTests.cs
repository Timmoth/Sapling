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
        Assert.Equal(expected, DataGenerator.IsRandomOpeningPly(randomMoveCount));
    }

    [Fact]
    public void InitialBoard_UsesChess960StartingPositionWhenConfigured()
    {
        var board = DataGenerator.CreateInitialBoard(useChess960: true, chess960Index: 0);

        Assert.True(board.Is960);
        Assert.True(board.WhiteToMove);
        Assert.NotEqual(CastleRights.None, board.CastleRights);
    }

    [Fact]
    public void InitialBoard_UsesStandardPositionWhenChess960IsDisabled()
    {
        var board = DataGenerator.CreateInitialBoard(useChess960: false);

        Assert.False(board.Is960);
        Assert.True(board.WhiteToMove);
    }

    [Theory]
    [InlineData(0, Viriformat.WdlDraw)]
    [InlineData(1, Viriformat.WdlBlackWin)]
    [InlineData(2, Viriformat.WdlWhiteWin)]
    public void GameOutcome_MapsToViriformatWdl(byte outcome, byte expectedWdl)
    {
        Assert.Equal(expectedWdl, DataGenerator.ToViriformatWdl(outcome));
    }

    [Fact]
    public void GameOutcome_RejectsUnknownValuesInsteadOfRecordingAWhiteWin()
    {
        var act = () => DataGenerator.ToViriformatWdl(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => act());
    }

    [Theory]
    [InlineData(true, 2500, 1)]
    [InlineData(true, -2500, 2)]
    [InlineData(false, 2500, 2)]
    [InlineData(false, -2500, 1)]
    public void Adjudication_MapsPreMoveSideToMoveScoreToTheCorrectWinner(
        bool whiteToMoveAfterMove, int score, byte expectedOutcome)
    {
        Assert.Equal(expectedOutcome, DataGenerator.GetAdjudicatedOutcome(whiteToMoveAfterMove, score));
    }

    [Theory]
    [InlineData("7k/6Q1/6K1/8/8/8/8/8 b - - 0 1", 2)] // black is checkmated: white wins
    [InlineData("8/8/8/8/8/1k6/1q6/K7 w - - 0 1", 1)] // white is checkmated: black wins
    [InlineData("7k/8/8/8/8/8/8/K7 w - - 0 1", 0)] // dead position draw
    public void GameStateOutcome_UsesAbsoluteWinnerColor(string fen, byte expectedOutcome)
    {
        using var game = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));

        Assert.Equal(expectedOutcome, game.WinDrawLoose());
    }

    [Fact]
    public void MirrorFen_IsAnInvolutionForAllChess960Starts()
    {
        foreach (var fen in Chess960.Fens)
        {
            Assert.Equal(fen, DataGenerator.MirrorFen(DataGenerator.MirrorFen(fen)));
        }
    }

    [Fact]
    public void MirrorFen_SwapsColoursRanksSideToMoveAndCastlingCase()
    {
        const string fen = "rbnkqbnr/pppppppp/8/8/8/8/PPPPPPPP/RBNKQBNR w HBhb - 0 1";

        var mirrored = DataGenerator.MirrorFen(fen);

        Assert.Equal("rbnkqbnr/pppppppp/8/8/8/8/PPPPPPPP/RBNKQBNR b BHbh - 0 1", mirrored);
    }

    [Fact]
    public void MirrorFen_ProducesParsableBoardWithBlackToMoveForAllChess960Starts()
    {
        foreach (var fen in Chess960.Fens)
        {
            var mirrored = DataGenerator.MirrorFen(fen);
            using var game = new GameState(BoardStateExtensions.CreateBoardFromFen(mirrored));

            Assert.False(game.Board.WhiteToMove);
            Assert.NotEqual(CastleRights.None, game.Board.CastleRights);
            Assert.True(game.LegalMoves.Count > 0);
        }
    }

    [Fact]
    public void MirrorFen_KeepsStandardStartPlacementAndFlipsSideToMove()
    {
        var mirrored = DataGenerator.MirrorFen(Constants.InitialState);

        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1", mirrored);
    }
}
