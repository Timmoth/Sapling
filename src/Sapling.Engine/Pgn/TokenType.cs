namespace Sapling.Engine.Pgn;

public enum TokenType
{
    Pawn,
    Rook,
    Bishop,
    Knight,
    Queen,
    King,
    Rank,
    File,
    QueenSideCastle,
    KingSideCastle,
    Capture,
    Check,
    Mate,
    Promotion,
    Checkmate,
    NewLine,
    Eof
}