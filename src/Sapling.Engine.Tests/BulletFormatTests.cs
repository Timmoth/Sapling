using System.IO;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Xunit;

namespace Sapling.Engine.Tests;

public class BulletFormatTests
{
    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(3, buffer.Length)]);
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(3, count));
    }

    [Fact]
    public void Read_ValidRecordArrivingInChunks_Succeeds()
    {
        using var reader = new BinaryReader(new ShortReadStream(new byte[BulletFormat.Size]));
        var record = new BulletFormat();
        record.Read(reader);
        Assert.Equal(BulletFormat.Size, reader.BaseStream.Position);
    }

    [Fact]
    public unsafe void Read_TruncatedRecord_ThrowsEndOfStream()
    {
        var bytes = new byte[BulletFormat.Size - 1];
        using var reader = new BinaryReader(new MemoryStream(bytes));

        var format = new BulletFormat();
        Assert.Throws<EndOfStreamException>(() => format.Read(reader));
    }

    [Fact]
    public unsafe void Read_FullRecord_RoundTripsThroughWrite()
    {
        var original = new BulletFormat
        {
            Occupancy = 0x0102030405060708,
            Score = -123,
            Result = 2,
            KingSquare = 60,
            OppKingSquare = 4
        };
        for (var i = 0; i < 16; i++)
        {
            original.Pieces[i] = (byte)(0xA0 + i);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            original.Write(writer);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var roundTripped = new BulletFormat();
        roundTripped.Read(reader);

        Assert.True(BulletFormat.IsEqual(original, roundTripped));
    }

    [Fact]
    public unsafe void Pack_SparseBoard_ZeroesUnusedPieceBytes()
    {
        // Regression: Pack OR-ed nibbles into an uncleared stackalloc buffer,
        // so piece bytes beyond the occupancy retained stack garbage.
        var board = BoardStateExtensions.CreateBoardFromFen("4K3/8/8/8/8/8/8/4k3 w - - 0 1");
        var packed = BulletFormat.Pack(ref board, 0, 2);

        // Two pieces fill only the first nibble pair (byte 0); bytes 1..15 must be zero.
        for (var i = 1; i < 16; i++)
        {
            Assert.True(packed.Pieces[i] == 0, $"piece byte {i} must be zero for a two-piece board");
        }
    }

    [Fact]
    public unsafe void Pack_InitialBoard_DecodesToAllThirtyTwoPieces()
    {
        var board = BoardStateExtensions.CreateBoardFromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        var packed = BulletFormat.Pack(ref board, 0, 2);

        // Invert the nibble encoding: pieceBits = (piece % 2) << 3 | (piece - 1) / 2.
        var nibbleToPiece = new int[16];
        Array.Fill(nibbleToPiece, -1);
        for (var piece = 1; piece <= 12; piece++)
        {
            var nibble = ((piece % 2) << 3) | ((piece - 1) / 2);
            nibbleToPiece[nibble] = piece;
        }

        var decoded = new int[32];
        for (var i = 0; i < 32; i++)
        {
            var nibble = (packed.Pieces[i >> 1] >> (4 * (i & 1))) & 0xF;
            decoded[i] = nibbleToPiece[nibble];
            Assert.True(decoded[i] > 0);
        }

        // Occupancy is scanned LSB-first (a1=0). Piece codes: white even (P=2..K=12), black odd (p=1..k=11).
        var expected = new[]
        {
            8, 4, 6, 10, 12, 6, 4, 8, // a1..h1: R N B Q K B N R
            2, 2, 2, 2, 2, 2, 2, 2, // a2..h2 pawns
            1, 1, 1, 1, 1, 1, 1, 1, // a7..h7 black pawns
            7, 3, 5, 9, 11, 5, 3, 7 // a8..h8: r n b q k b n r
        };
        Assert.Equal(expected, decoded);
    }
}
