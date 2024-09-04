namespace iBlunder.Engine;

public sealed record MagicBitBoard(ulong MagicNumber, ulong MovementMask, int Position, ulong[] Moves)
{
    public ulong GetMoves(ulong blockers)
    {
        return Moves[((MovementMask & blockers) * MagicNumber) >> Position];
    }
}