namespace iBlunder.Engine.Pgn;

public readonly record struct Token(TokenType TokenType, int Start, int Length);