using Sapling.Engine.MoveGen;
using System.Runtime.InteropServices;

namespace Sapling.Engine;

public sealed unsafe class GameState
{
    public readonly List<uint> History;
    public readonly List<uint> LegalMoves;
    public readonly ulong* Moves;
    public BoardStateData Board = default;

    public static ulong* AllocateMoves()
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)sizeof(ulong) * 800, alignment);
        NativeMemory.Clear(block, (nuint)sizeof(ulong) * 800);

        return (ulong*)block;
    }

    ~GameState()
    {
        NativeMemory.AlignedFree(Moves);
    }

    public GameState(BoardStateData board)
    {
        Moves = AllocateMoves();
        Board = board;
        History = new List<uint>();
        LegalMoves = new List<uint>();
        Board.GenerateLegalMoves(LegalMoves, false);
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
        Moves[Board.TurnCount] = Board.Hash;
    }
    public void ResetToFen(string fen)
    {
        History.Clear();
        var state = BoardStateExtensions.CreateBoardFromFen(fen);
        state.CloneTo(ref Board);
        Board.GenerateLegalMoves(LegalMoves, false);
        Moves[Board.TurnCount] = Board.Hash;
    }

    public void Reset()
    {
        History.Clear();
        var state = Constants.InitialBoard;
        state.CloneTo(ref Board);
        Board.GenerateLegalMoves(LegalMoves, false);
        Moves[Board.TurnCount] = Board.Hash;
    }

    public void ResetTo(ref BoardStateData newBoard, uint[] legalMoves)
    {
        History.Clear();
        newBoard.CloneTo(ref Board);
        LegalMoves.Clear();
        LegalMoves.AddRange(legalMoves);
        Moves[Board.TurnCount] = Board.Hash;
    }
    public bool Apply(uint move)
    {
        if (!LegalMoves.Contains(move))
        {
            return false;
        }

        var oldEnpassant = Board.EnPassantFile;
        var oldCastle = Board.CastleRights;

        AccumulatorState emptyAccumulator = default;
        Board.PartialApply(move);
        Board.UpdateCheckStatus();
        Board.UpdateCastleStatus(Board.CastleRights);
        Board.FinishApply(ref emptyAccumulator, move, oldEnpassant, oldCastle);
        Board.GenerateLegalMoves(LegalMoves, false);
        History.Add(move);
        Moves[Board.TurnCount] = Board.Hash;

        return true;
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