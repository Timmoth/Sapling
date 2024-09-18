using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Tests;

public static class ChessboardHelpers
{
    public static (GameState gameState, Searcher searcher) InitialState()
    {
        var board = BoardStateExtensions.CreateBoardFromArray(Constants.InitialState);
        var search = new Searcher(new Transposition[0xFF]);
        return (new GameState(board), search);
    }

    public static (GameState gameState, Searcher searcher) Create(Piece[] pieces)
    {
        var board = BoardStateExtensions.CreateBoardFromArray(pieces);
        var search = new Searcher(new Transposition[0xFF]);
        return (new GameState(board), search);
    }
}