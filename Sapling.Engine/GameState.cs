using Sapling.Engine.MoveGen;
using System.Runtime.InteropServices;

namespace Sapling.Engine;

public sealed unsafe class GameState
{
    public readonly List<uint> History;
    public readonly List<uint> LegalMoves;
    public readonly ulong* HashHistory;
    public BoardStateData Board = default;

    ~GameState()
    {
        NativeMemory.AlignedFree(HashHistory);
    }

    public GameState(BoardStateData board)
    {
        HashHistory = MemoryHelpers.Allocate<ulong>(800);
        Board = board;
        History = new List<uint>();
        LegalMoves = new List<uint>();
        Board.GenerateLegalMoves(LegalMoves, false);
        *(HashHistory + Board.TurnCount - 1) = Board.Hash;
    }

    public static GameState InitialState()
    {
        return new GameState(BoardStateExtensions.CreateBoardFromFen(Constants.InitialState));
    }

    public void ResetTo(ref BoardStateData newBoard)
    {
        History.Clear();
        newBoard.CloneTo(ref Board);
        Board.GenerateLegalMoves(LegalMoves, false);
        *(HashHistory + Board.TurnCount - 1) = Board.Hash;
    }
    public void ResetToFen(string fen)
    {
        History.Clear();
        var state = BoardStateExtensions.CreateBoardFromFen(fen);
        state.CloneTo(ref Board);
        Board.GenerateLegalMoves(LegalMoves, false);
        *(HashHistory + Board.TurnCount - 1) = Board.Hash;
    }

    public void Reset()
    {
        History.Clear();
        var state = Constants.InitialBoard;
        state.CloneTo(ref Board);
        Board.GenerateLegalMoves(LegalMoves, false);
        *(HashHistory + Board.TurnCount - 1) = Board.Hash;
    }

    public void ResetTo(ref BoardStateData newBoard, uint[] legalMoves)
    {
        History.Clear();
        newBoard.CloneTo(ref Board);
        LegalMoves.Clear();
        LegalMoves.AddRange(legalMoves);
        *(HashHistory + Board.TurnCount - 1) = Board.Hash;
    }
    public void Apply(uint move)
    {
        var oldEnpassant = Board.EnPassantFile;
        var oldCastle = Board.CastleRights;

        AccumulatorState emptyAccumulator = default;
        var whiteToMove = Board.WhiteToMove;
        if (whiteToMove)
        {
            Board.PartialApplyWhite(move);
        }
        else
        {
            Board.PartialApplyBlack(move);
        }

        Board.UpdateCheckStatus();

        fixed (BoardStateData* boardPtr = &Board)
        {
            // Copy the memory block from source to destination

            if (whiteToMove)
            {
                boardPtr->FinishApplyWhite(ref emptyAccumulator, move, oldEnpassant, oldCastle);
            }
            else
            {
                boardPtr->FinishApplyBlack(ref emptyAccumulator, move, oldEnpassant, oldCastle);
            }
        }

        Board.GenerateLegalMoves(LegalMoves, false);
        History.Add(move);
        *(HashHistory + Board.TurnCount - 1) = Board.Hash;
    }

    public bool GameOver()
    {
        return LegalMoves.Count == 0 || Board.HalfMoveClock >= 100 || Board.InsufficientMatingMaterial();
    }

    public byte WinDrawLoose()
    {
        if (LegalMoves.Count != 0)
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