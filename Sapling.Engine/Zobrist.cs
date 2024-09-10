namespace Sapling.Engine;

public static class Zobrist
{
    private const int PieceCount = 13;

    public const ulong WhiteKingSideCastlingRights = 11311897866893552773UL;
    public const ulong WhiteQueenSideCastlingRights = 1036162298138562081UL;
    public const ulong BlackKingSideCastlingRights = 18378780805759378610UL;
    public const ulong BlackQueenSideCastlingRights = 10633751885042570073UL;
    public const ulong SideToMove = 17258756683597918105UL;
    public static readonly ulong[] PiecesArray = new ulong[PieceCount * 64];

    public static readonly ulong[] EnPassantFile = new ulong[8];

    static Zobrist()
    {
        const int seed = 69;
        var rng = new Random(seed);

        for (var squareIndex = 0; squareIndex < 64; squareIndex++)
        {
            for (var i = 0; i < PieceCount; i++)
            {
                PiecesArray[i * 64 + squareIndex] = RandomUnsigned64BitNumber(rng);
            }
        }

        for (var i = 0; i < EnPassantFile.Length; i++)
        {
            PiecesArray[i] = RandomUnsigned64BitNumber(rng);
        }
    }

    public static ulong CalculateZobristKey(BoardState board)
    {
        ulong zobristKey = 0;

        for (byte squareIndex = 0; squareIndex < 64; squareIndex++)
        {
            var piece = board.Pieces[squareIndex];

            if (piece != Constants.None)
            {
                zobristKey ^= PiecesArray[piece * 64 + squareIndex];
            }
        }

        if (!board.WhiteToMove)
        {
            zobristKey ^= SideToMove;
        }

        zobristKey ^= WhiteKingSideCastlingRights;
        zobristKey ^= WhiteQueenSideCastlingRights;
        zobristKey ^= BlackKingSideCastlingRights;
        zobristKey ^= BlackQueenSideCastlingRights;

        if (board.EnPassantFile != 8)
        {
            zobristKey ^= EnPassantFile[board.EnPassantFile];
        }

        return zobristKey;
    }

    private static ulong RandomUnsigned64BitNumber(Random rng)
    {
        var buffer = new byte[8];
        rng.NextBytes(buffer);
        return BitConverter.ToUInt64(buffer, 0);
    }
}