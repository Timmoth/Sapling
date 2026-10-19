using System.Buffers.Binary;
using System.IO;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Sapling.Engine.MoveGen;
using Xunit;

namespace Sapling.Engine.Tests;

public class ViriformatTests
{
    [Fact]
    public void EncodeMove_QuietPawns_MapsSquaresAndKeepsNoFlags()
    {
        var move = MoveExtensions.EncodeNormalMove(Constants.WhitePawn, 12, 28); // e2e4
        var encoded = Viriformat.EncodeMove(move, false);

        Assert.Equal((ushort)(12 | (28 << 6)), encoded); // squares pass through unchanged
        Assert.Equal(0, (encoded & 0xC000));
    }

    [Fact]
    public void EncodeMove_EnPassant_SetsEpFlagAndDestinationSquare()
    {
        var move = MoveExtensions.EncodeWhiteEnpassantMove(36, 4); // d5 -> e6 capturing ep
        var encoded = Viriformat.EncodeMove(move, false);

        var from = encoded & 0x3F;
        var to = (encoded >> 6) & 0x3F;
        Assert.Equal(36, from);
        Assert.Equal(40 + 4, to);
        Assert.Equal(0x4000, (encoded & 0xC000));
    }

    [Fact]
    public void EncodeMove_CastleStandard_RemapsKingDestinationToRookSquare()
    {
        var whiteKingSide = MoveExtensions.EncodeCastleMove(Constants.WhiteKing, 4, 6);
        Assert.Equal((ushort)(4 | (7 << 6) | 0x8000), Viriformat.EncodeMove(whiteKingSide, false)); // e1 -> h1 rook square

        var whiteQueenSide = MoveExtensions.EncodeCastleMove(Constants.WhiteKing, 4, 2);
        Assert.Equal((ushort)(4 | (0 << 6) | 0x8000), Viriformat.EncodeMove(whiteQueenSide, false)); // e1 -> a1 rook square

        var blackKingSide = MoveExtensions.EncodeCastleMove(Constants.BlackKing, 60, 62);
        Assert.Equal((ushort)(60 | (63 << 6) | 0x8000), Viriformat.EncodeMove(blackKingSide, false)); // e8 -> h8 rook square

        var blackQueenSide = MoveExtensions.EncodeCastleMove(Constants.BlackKing, 60, 58);
        Assert.Equal((ushort)(60 | (56 << 6) | 0x8000), Viriformat.EncodeMove(blackQueenSide, false)); // e8 -> a8 rook square
    }

    [Fact]
    public void EncodeMove_Castle960_KeepsRookSquareTarget()
    {
        // In chess960 mode Sapling already encodes the rook square as the target.
        var move = MoveExtensions.EncodeCastleMove(Constants.WhiteKing, 4, 7);
        Assert.Equal((ushort)(4 | (7 << 6) | 0x8000), Viriformat.EncodeMove(move, true));
    }

    [Fact]
    public void EncodeMove_Promotions_MapPieceTypesAndSetPromotionFlag()
    {
        var knight = MoveExtensions.EncodePromotionMove(Constants.WhitePawn, 52, 60, Constants.PawnKnightPromotion);
        Assert.Equal(0x0000 | 0xC000, (Viriformat.EncodeMove(knight, false) & 0xF000));

        var queen = MoveExtensions.EncodePromotionMove(Constants.WhitePawn, 52, 60, Constants.PawnQueenPromotion);
        var encoded = Viriformat.EncodeMove(queen, false);
        Assert.Equal((ushort)(52 | (60 << 6) | (3 << 12) | 0xC000), encoded);
    }

    [Fact]
    public unsafe void WriteHeader_InitialPosition_EncodesUnmovedRooksAndNibbles()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(Constants.InitialState);
        Span<byte> header = stackalloc byte[Viriformat.HeaderSize];
        Viriformat.WriteHeader(header, ref board, Viriformat.WdlDraw);

        Assert.Equal(0xFFFF00000000FFFF, BinaryPrimitives.ReadUInt64LittleEndian(header)); // symmetric start position, rank-flip invariant

        var expected = new byte[]
        {
            6, 1, 2, 4, 5, 2, 1, 6,       // a1..h1: rooks unmoved (code 6)
            0, 0, 0, 0, 0, 0, 0, 0,       // a2..h2 white pawns
            8, 8, 8, 8, 8, 8, 8, 8,       // a7..h7 black pawns
            14, 9, 10, 12, 13, 10, 9, 14  // a8..h8: black rooks unmoved (code 6|8)
        };

        for (var i = 0; i < 32; i++)
        {
            var nibble = (header[8 + (i >> 1)] >> ((i & 1) * 4)) & 0xF;
            Assert.True(nibble == expected[i], $"piece slot {i}");
        }

        Assert.Equal(64, header[24]); // white to move, no ep
        Assert.Equal(0, header[25]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(header[26..]));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(header[28..]));
        Assert.Equal(Viriformat.WdlDraw, header[30]);
        Assert.Equal(0, header[31]);
    }

    [Fact]
    public unsafe void WriteHeader_BlackToMoveStart_SetsSideToMoveBit()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1");
        Span<byte> header = stackalloc byte[Viriformat.HeaderSize];
        Viriformat.WriteHeader(header, ref board, Viriformat.WdlDraw);

        Assert.Equal(64 | 0x80, header[24]); // black to move, no ep
    }

    [Fact]
    public unsafe void WriteHeader_Chess960_MarksOnlyCastlingRooks()
    {
        // Back rank: rooks on b1/g1 (files 1/6), king on d1 (file 3) -> Shredder rights "BHbh".
        var board = BoardStateExtensions.CreateBoardFromFen("nrbkqbnr/pppppppp/8/8/8/8/PPPPPPPP/NRBKQBNR w BHbh - 0 1");
        Assert.True(board.Is960);

        Span<byte> header = stackalloc byte[Viriformat.HeaderSize];
        Viriformat.WriteHeader(header, ref board, Viriformat.WdlWhiteWin);

        // White rank (a1..h1) occupies piece slots 0..7.
        var whiteRank = new byte[8];
        for (var i = 0; i < 8; i++)
        {
            var slot = i;
            whiteRank[i] = (byte)((header[8 + (slot >> 1)] >> ((slot & 1) * 4)) & 0xF);
        }

        // a: knight, b: unmoved rook, c: bishop, d: king, e: queen, f: bishop, g: knight, h: unmoved rook
        Assert.Equal(new byte[] { 1, 6, 2, 5, 4, 2, 1, 6 }, whiteRank);

        Assert.Equal(Viriformat.WdlWhiteWin, header[30]);
    }

    [Fact]
    public unsafe void WriteGame_WritesHeaderMovesAndNullTerminator()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(Constants.InitialState);

        Span<(uint move, short eval)> moves = stackalloc (uint move, short eval)[2];
        moves[0] = (MoveExtensions.EncodeNormalMove(Constants.WhitePawn, 12, 28), 42);
        moves[1] = (MoveExtensions.EncodeNormalMove(Constants.BlackPawn, 52, 36), -37);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            Viriformat.WriteGame(stackalloc byte[Viriformat.MaxGameBytes], writer, ref board, moves, 2, Viriformat.WdlDraw);
        }

        var bytes = stream.ToArray();
        Assert.Equal(Viriformat.HeaderSize + 2 * Viriformat.MoveRecordSize + Viriformat.MoveRecordSize, bytes.Length);

        Assert.Equal(Viriformat.EncodeMove(moves[0].move, false), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(Viriformat.HeaderSize, 2)));
        Assert.Equal(42, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(Viriformat.HeaderSize + 2, 2)));
        Assert.Equal(37, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(Viriformat.HeaderSize + Viriformat.MoveRecordSize + 2, 2)));

        Assert.All(bytes[^4..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void WriteGame_BlackToMoveStart_StoresWhiteRelativeScores()
    {
        var board = BoardStateExtensions.CreateBoardFromFen(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1");
        Span<(uint move, short eval)> moves = stackalloc (uint move, short eval)[2];
        moves[0] = (MoveExtensions.EncodeNormalMove(Constants.BlackPawn, 52, 36), 42);
        moves[1] = (MoveExtensions.EncodeNormalMove(Constants.WhitePawn, 12, 28), -37);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            Viriformat.WriteGame(stackalloc byte[Viriformat.MaxGameBytes], writer, ref board, moves, 2, Viriformat.WdlDraw);
        }

        var bytes = stream.ToArray();
        Assert.Equal(-42, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(Viriformat.HeaderSize + 2, 2)));
        Assert.Equal(-37, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(Viriformat.HeaderSize + Viriformat.MoveRecordSize + 2, 2)));
    }
}
