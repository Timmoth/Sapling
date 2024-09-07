using iBlunder.Engine.Evaluation;

namespace iBlunder.Engine;

public sealed class BoardState
{
    public readonly byte[] Pieces = new byte[64];
    public ulong BlackBishops;
    public ulong BlackKings;
    public byte BlackKingSquare = 0;
    public ulong BlackKnights;
    public ulong BlackPawns;
    public ulong BlackPieces;
    public ulong BlackQueens;
    public ulong BlackRooks;

    public CastleRights CastleRights = Constants.AllCastleRights;
    public byte EnPassantFile;
    public NnueEvaluator Evaluator = default!;
    public int HalfMoveClock;
    public ulong Hash;

    public bool InCheck = false;
    public ulong Occupancy;
    public byte PieceCount;
    public Stack<ulong> RepetitionPositionHistory = new(64);
    public ushort TurnCount;
    public ulong WhiteBishops;
    public ulong WhiteKings;
    public byte WhiteKingSquare = 0;
    public ulong WhiteKnights;
    public ulong WhitePawns;
    public ulong WhitePieces;
    public ulong WhiteQueens;
    public ulong WhiteRooks;
    public bool WhiteToMove;
}