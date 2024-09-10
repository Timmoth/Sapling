namespace Sapling.Engine;

[Flags]
public enum CastleRights : byte
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8
}

public static class Constants
{
    public const int ImmediateMateScore = 100000;

    #region Bitboards

    public const ulong NotAFile = 0xFEFEFEFEFEFEFEFE; // All squares except column 'A'
    public const ulong NotHFile = 0x7F7F7F7F7F7F7F7F; // All squares except column 'H'

    public const byte None = 0;

    public const byte BlackPawn = 1;
    public const byte BlackKnight = 3;
    public const byte BlackBishop = 5;
    public const byte BlackRook = 7;
    public const byte BlackQueen = 9;
    public const byte BlackKing = 11;

    public const byte WhitePawn = 2;
    public const byte WhiteKnight = 4;
    public const byte WhiteBishop = 6;
    public const byte WhiteRook = 8;
    public const byte WhiteQueen = 10;
    public const byte WhiteKing = 12;

    public const byte Castle = 1;
    public const byte DoublePush = 2;
    public const byte EnPassant = 3;
    public const byte PawnKnightPromotion = 4;
    public const byte PawnBishopPromotion = 5;
    public const byte PawnRookPromotion = 6;
    public const byte PawnQueenPromotion = 7;

    public const CastleRights AllCastleRights = CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide |
                                                CastleRights.BlackKingSide | CastleRights.BlackQueenSide;

    public const CastleRights WhiteCastleRights = CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide;

    public const CastleRights BlackCastleRights = CastleRights.BlackKingSide | CastleRights.BlackQueenSide;

    #endregion

    #region Move Ordering

    public const int WinningCaptureBias = 10_000_000;
    public const int LosingCaptureBias = 16_000;
    public const int PromoteBias = 600_000;
    public const int KillerABias = 500_000;
    public const int KillerBBias = 250_000;
    public const int CounterMoveBias = 65_000;

    #endregion

    #region PieceValue

    public const int PawnValue = 100;
    public const int KnightValue = 450;
    public const int BishopValue = 450;
    public const int RookValue = 650;
    public const int QueenValue = 1250;
    public const int KingValue = 5000;

    public const int MaxSearchDepth = 80;
    public const int MaxScore = 99999999;
    public const int MinScore = -99999999;

    public static readonly Piece[] InitialState =
    {
        Piece.WhiteRook, Piece.WhiteKnight, Piece.WhiteBishop, Piece.WhiteQueen, Piece.WhiteKing, Piece.WhiteBishop,
        Piece.WhiteKnight, Piece.WhiteRook,
        Piece.WhitePawn, Piece.WhitePawn, Piece.WhitePawn, Piece.WhitePawn, Piece.WhitePawn, Piece.WhitePawn,
        Piece.WhitePawn, Piece.WhitePawn,
        Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None,
        Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None,
        Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None,
        Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None, Piece.None,
        Piece.BlackPawn, Piece.BlackPawn, Piece.BlackPawn, Piece.BlackPawn, Piece.BlackPawn, Piece.BlackPawn,
        Piece.BlackPawn, Piece.BlackPawn,
        Piece.BlackRook, Piece.BlackKnight, Piece.BlackBishop, Piece.BlackQueen, Piece.BlackKing, Piece.BlackBishop,
        Piece.BlackKnight, Piece.BlackRook
    };

    #endregion
}