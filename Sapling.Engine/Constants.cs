using Sapling.Engine;

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
    public const short ImmediateMateScore = 29_000;

    #region Bitboards

    public const ulong NotAFile = 0xFEFEFEFEFEFEFEFE; // All squares except column 'A'
    public const ulong NotHFile = 0x7F7F7F7F7F7F7F7F; // All squares except column 'H'

    public const byte None = 0;

    public const byte Occupancy = 0;
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

    public const byte WhitePieces = 13;
    public const byte BlackPieces = 14;

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


    public const int BlackPawnZobristOffset = 1 * 64;
    public const int BlackKnightZobristOffset = 3 * 64;
    public const int BlackBishopZobristOffset = 5 * 64;
    public const int BlackRookZobristOffset = 7 * 64;
    public const int BlackQueenZobristOffset = 9 * 64;
    public const int BlackKingZobristOffset = 11 * 64;
    public const int WhitePawnZobristOffset = 2 * 64;
    public const int WhiteKnightZobristOffset = 4 * 64;
    public const int WhiteBishopZobristOffset = 6 * 64;
    public const int WhiteRookZobristOffset = 8 * 64;
    public const int WhiteQueenZobristOffset = 10 * 64;
    public const int WhiteKingZobristOffset = 12 * 64;

    public const int BlackPawnFeatureIndexOffset = 1 * 128;
    public const int BlackKnightFeatureIndexOffset = 3 * 128;
    public const int BlackBishopFeatureIndexOffset = 5 * 128;
    public const int BlackRookFeatureIndexOffset = 7 * 128;
    public const int BlackQueenFeatureIndexOffset = 9 * 128;
    public const int BlackKingFeatureIndexOffset = 11 * 128;
    public const int WhitePawnFeatureIndexOffset = 2 * 128;
    public const int WhiteKnightFeatureIndexOffset = 4 * 128;
    public const int WhiteBishopFeatureIndexOffset = 6 * 128;
    public const int WhiteRookFeatureIndexOffset = 8 * 128;
    public const int WhiteQueenFeatureIndexOffset = 10 * 128;
    public const int WhiteKingFeatureIndexOffset = 12 * 128;

    public const int BlackKingSideCastleKingFromIndex = BlackKingFeatureIndexOffset + (60 << 1);
    public const int BlackKingSideCastleRookFromIndex = BlackRookFeatureIndexOffset + (63 << 1);
    public const int BlackKingSideCastleKingToIndex = BlackKingFeatureIndexOffset + (62 << 1);
    public const int BlackKingSideCastleRookToIndex = BlackRookFeatureIndexOffset + (61 << 1);
    public const int BlackQueenSideCastleKingFromIndex = BlackKingFeatureIndexOffset + (60 << 1);
    public const int BlackQueenSideCastleRookFromIndex = BlackRookFeatureIndexOffset + (56 << 1);
    public const int BlackQueenSideCastleKingToIndex = BlackKingFeatureIndexOffset + (58 << 1);
    public const int BlackQueenSideCastleRookToIndex = BlackRookFeatureIndexOffset + (59 << 1);

    public const int WhiteKingSideCastleKingFromIndex = WhiteKingFeatureIndexOffset + (4 << 1);
    public const int WhiteKingSideCastleRookFromIndex = WhiteRookFeatureIndexOffset + (7 << 1);
    public const int WhiteKingSideCastleKingToIndex = WhiteKingFeatureIndexOffset + (6 << 1);
    public const int WhiteKingSideCastleRookToIndex = WhiteRookFeatureIndexOffset + (5 << 1);
    public const int WhiteQueenSideCastleKingFromIndex = WhiteKingFeatureIndexOffset + (4 << 1);
    public const int WhiteQueenSideCastleRookFromIndex = WhiteRookFeatureIndexOffset;
    public const int WhiteQueenSideCastleKingToIndex = WhiteKingFeatureIndexOffset + (2 << 1);
    public const int WhiteQueenSideCastleRookToIndex = WhiteRookFeatureIndexOffset + (3 << 1);

    #endregion

    #region PieceValue

    public const int PawnValue = 100;
    public const int KnightValue = 450;
    public const int BishopValue = 450;
    public const int RookValue = 650;
    public const int QueenValue = 1250;
    public const int KingValue = 5000;

    public const int MaxSearchDepth = 120;
    public const int MaxScore = 99999999;
    public const int MinScore = -99999999;

    public static readonly string InitialState = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    public static readonly BoardStateData InitialBoard = BoardStateExtensions.CreateBoardFromFen(InitialState);

    #endregion
}