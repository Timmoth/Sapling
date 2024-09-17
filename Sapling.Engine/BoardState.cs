using Sapling.Engine.Evaluation;
using System.Runtime.InteropServices;

namespace Sapling.Engine;

public sealed unsafe class BoardState
{
    public readonly byte* Pieces;
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
    public readonly ulong* Moves;
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

    public BoardState()
    {
        Pieces = AllocatePieces();
        Moves = AllocateMoves();
    }
    public static byte* AllocatePieces()
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)64, alignment);
        NativeMemory.Clear(block, (nuint)64);

        return (byte*)block;
    }
    public static ulong* AllocateMoves()
    {
        const nuint alignment = 64;

        var block = NativeMemory.AlignedAlloc((nuint)sizeof(ulong) * 800, alignment);
        NativeMemory.Clear(block, (nuint)sizeof(ulong) * 800);

        return (ulong*)block;
    }


    ~BoardState()
    {
        NativeMemory.AlignedFree(Pieces);
        NativeMemory.AlignedFree(Moves);
    }
}