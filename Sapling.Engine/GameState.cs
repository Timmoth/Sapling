using Sapling.Engine.MoveGen;

namespace Sapling.Engine;

public sealed class GameState
{
    public readonly BoardState Board;
    public readonly List<uint> History;
    public readonly List<uint> Moves;
    public readonly List<Piece> TakenPieces;

    public GameState(BoardState board)
    {
        Board = board;
        History = new List<uint>();
        TakenPieces = new List<Piece>();
        Moves = new List<uint>();
        board.GenerateLegalMoves(Moves, false);
    }

    public static GameState InitialState()
    {
        return new GameState(BoardStateExtensions.CreateBoardFromArray(Constants.InitialState));
    }

    public void ResetTo(BoardState board)
    {
        History.Clear();
        TakenPieces.Clear();

        Board.ResetTo(board);
        Board.GenerateLegalMoves(Moves, false);
    }   
    
    public void ResetTo(BoardState board, uint[] legalMoves)
    {
        History.Clear();
        TakenPieces.Clear();

        Board.ResetTo(board);
        Moves.Clear();
        Moves.AddRange(legalMoves);
    }

    public bool Apply(uint move)
    {
        if (!Moves.Contains(move))
        {
            return false;
        }

        Board.Apply(move);

        Board.GenerateLegalMoves(Moves, false);
        History.Add(move);

        if (move.IsCapture())
        {
            TakenPieces.Add((Piece)move.GetCapturedPiece());
        }

        return true;
    }

    public bool GameOver()
    {
        return Moves.Count == 0 || Board.HalfMoveClock >= 100 || Board.InsufficientMatingMaterial();
    }

    public byte WinDrawLoose()
    {
        if (Moves.Count != 0)
        {
            // Draw
            return 0;
        }

        if (Board.WhiteToMove)
        {
            // Black wins
            return 1;
        }

        // White wins
        return 2;
    }
}