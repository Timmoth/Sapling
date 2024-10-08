namespace Sapling.Engine.MoveGen;

public enum MoveType : byte
{
    Normal = 0,
    Castle = 1,
    DoublePush = 2,
    EnPassant = 3,
    PawnRookPromotion = 4,
    PawnKnightPromotion = 5,
    PawnBishopPromotion = 6,
    PawnQueenPromotion = 7
}