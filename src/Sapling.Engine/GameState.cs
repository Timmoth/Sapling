using Sapling.Engine.MoveGen;
using System.Runtime.InteropServices;

namespace Sapling.Engine;

public sealed unsafe class GameState : IDisposable
{
    // HashHistory is indexed by Board.TurnCount (a ushort set from the FEN fullmove number and
    // incremented every ply). Capacity must cover the maximum valid index (65535).
    public const int HashHistoryLength = ushort.MaxValue + 1;

    private int _disposed;

    public readonly List<uint> History;
    public readonly List<uint> LegalMoves;
    public readonly ulong* HashHistory;
    public BoardStateData Board = default;

    ~GameState() => ReleaseHistory();

    private void ReleaseHistory()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            MemoryHelpers.FreeAligned(HashHistory);
    }

    public void Dispose()
    {
        ReleaseHistory();
        GC.SuppressFinalize(this);
    }

    internal void EnsureUsable() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static void ValidateBoard(in BoardStateData board)
    {
        if (board.TurnCount == 0)
            throw new ArgumentOutOfRangeException(nameof(board), "The fullmove/history counter must be positive.");
    }

    public GameState(BoardStateData board)
    {
        ValidateBoard(board);
        HashHistory = MemoryHelpers.Allocate<ulong>(HashHistoryLength);
        try
        {
        Board = board;
        History = new List<uint>();
        LegalMoves = new List<uint>();
        Board.GenerateLegalMoves(LegalMoves, false);
        ResetHashHistory();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void ResetHashHistory()
    {
        // A reset/FEN supplies no preceding positions. Never reuse hashes from a prior
        // game as repetition evidence; clear precisely the range a search can copy.
        NativeMemory.Clear(HashHistory, (nuint)Board.TurnCount * sizeof(ulong));
        HashHistory[Board.TurnCount - 1] = Board.Hash;
    }

    public static GameState InitialState()
    {
        return new GameState(BoardStateExtensions.CreateBoardFromFen(Constants.InitialState));
    }

    public void ResetTo(ref BoardStateData newBoard)
    {
        EnsureUsable();
        ValidateBoard(newBoard);
        History.Clear();
        newBoard.CloneTo(ref Board);
        Board.GenerateLegalMoves(LegalMoves, false);
        ResetHashHistory();
    }
    public void ResetToFen(string fen)
    {
        EnsureUsable();
        var state = BoardStateExtensions.CreateBoardFromFen(fen);
        History.Clear();
        state.CloneTo(ref Board);
        Board.GenerateLegalMoves(LegalMoves, false);
        ResetHashHistory();
    }

    public void Reset()
    {
        EnsureUsable();
        History.Clear();
        var state = Constants.InitialBoard;
        state.CloneTo(ref Board);
        Board.GenerateLegalMoves(LegalMoves, false);
        ResetHashHistory();
    }

    public void ResetTo(ref BoardStateData newBoard, uint[] legalMoves)
    {
        EnsureUsable();
        ValidateBoard(newBoard);
        History.Clear();
        newBoard.CloneTo(ref Board);
        LegalMoves.Clear();
        LegalMoves.AddRange(legalMoves);
        ResetHashHistory();
    }
    public void Apply(uint move)
    {
        EnsureUsable();
        if (Board.TurnCount == ushort.MaxValue)
            throw new InvalidOperationException("History counter limit reached; rebase the position before applying another move.");
        var oldEnpassant = Board.EnPassantFile;
        var oldEnpassantHashFile = Zobrist.GetEnPassantHashFile(ref Board);
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
                boardPtr->FinishApplyWhite(ref emptyAccumulator, move, oldEnpassant, oldEnpassantHashFile, oldCastle);
            }
            else
            {
                boardPtr->FinishApplyBlack(ref emptyAccumulator, move, oldEnpassant, oldEnpassantHashFile, oldCastle);
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
