using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sapling.Engine.DataGen;

[StructLayout(LayoutKind.Explicit, Size = 32)]
public unsafe struct BulletFormat
{
    public const int Size = 32;

    [FieldOffset(0)] public ulong Occupancy = 0;
    [FieldOffset(8)] public fixed byte Pieces[16];
    [FieldOffset(24)] public short Score = 0;
    [FieldOffset(26)] public byte Result = 0;
    [FieldOffset(27)] public byte KingSquare = 0;
    [FieldOffset(28)] public byte OppKingSquare = 0;
    [FieldOffset(29)] public fixed byte _pad[3];

    public byte[] GetPieces()
    {
        var buffer = new byte[16];

        fixed (byte* piecesPtr = Pieces) // This gets the pointer to the Pieces field
        {
            // Copy the data from the Pieces field to the new array
            new Span<byte>(piecesPtr, 16).CopyTo(buffer);
        }

        return buffer;
    }

    public BulletFormat()
    {
    }

    public static bool IsEqual(BulletFormat a, BulletFormat b)
    {
        // Cast the structs to byte pointers
        var aPtr = (byte*)&a;
        var bPtr = (byte*)&b;

        // Iterate over each byte in the struct
        for (var i = 0; i < Size; i++)
        {
            if (*(aPtr + i) != *(bPtr + i))
            {
                return false;
            }
        }

        return true;
    }

    public void Read(BinaryReader reader)
    {
        // Create a temporary buffer to read the data from the BinaryReader
        Span<byte> buffer = stackalloc byte[Size];

        // Read the data into the buffer
        reader.Read(buffer);

        // Copy the data from the buffer into this struct
        fixed (void* buffPtr = &buffer[0], thisPtr = &this)
        {
            Unsafe.CopyBlock(thisPtr, buffPtr, Size);
        }
    }

    public void Write(BinaryWriter writer)
    {
        // Create a temporary buffer to hold the struct data
        Span<byte> buffer = stackalloc byte[Size];

        // Copy the struct data into the buffer
        fixed (void* buffPtr = &buffer[0], thisPtr = &this)
        {
            Unsafe.CopyBlock(buffPtr, thisPtr, Size);
        }

        // Write the buffer to the BinaryWriter's underlying stream
        writer.Write(buffer);
    }

    public static BulletFormat Pack(ref BoardStateData board, short score, byte wdl)
    {
        var data = new BulletFormat();
        Span<byte> pieces = stackalloc byte[16];

        if (board.WhiteToMove)
        {
            data.KingSquare = board.WhiteKingSquare;
            data.OppKingSquare = (byte)(board.BlackKingSquare ^ 0x38);
            data.Score = score;
            data.Result = wdl;
            data.Occupancy = board.Occupancy[Constants.Occupancy];

            var nextPiece = 0;

            var bits = data.Occupancy;
            while (bits != 0)
            {
                var index = bits.PopLSB();
                var piece = board.GetPiece(index);
                var pieceBits = (piece % 2) << 3;
                pieceBits |= (piece - 1) / 2;
                var offset = 4 * (nextPiece & 1);
                pieces[nextPiece >> 1] |= (byte)(pieceBits << offset);
                nextPiece++;
            }
        }
        else
        {
            data.KingSquare = (byte)(board.BlackKingSquare ^ 0x38);
            data.OppKingSquare = board.WhiteKingSquare;
            data.Score = score;
            data.Result = (byte)(-1 * (wdl - 1) + 1);
            data.Occupancy = BinaryPrimitives.ReverseEndianness(board.Occupancy[Constants.Occupancy]);

            var nextPiece = 0;

            var bits = data.Occupancy;
            while (bits != 0)
            {
                var index = bits.PopLSB();
                var piece = board.GetPiece(index ^ 0x38);
                var pieceBits = ((piece % 2) ^ 1) << 3;
                pieceBits |= (piece - 1) / 2;
                var offset = 4 * (nextPiece & 1);
                pieces[nextPiece >> 1] |= (byte)(pieceBits << offset);
                nextPiece++;
            }
        }

        for (var i = 0; i < pieces.Length; i++)
        {
            data.Pieces[i] = pieces[i];
        }

        return data;
    }

    public void UpdateWdl(bool whiteToMove, byte wdl)
    {
        // Input
        // wdl 0 -> draw
        // wdl 1 -> black win
        // wdl 2 -> white win

        // Output
        // 0 -> Stm looses
        // 1 -> Draw
        // 2 -> Stm wins

        if (whiteToMove)
        {
            Result = wdl switch
            {
                0 => 1,
                1 => 0,
                _ => 2
            };
        }
        else
        {
            Result = wdl switch
            {
                0 => 1,
                2 => 0,
                _ => 2
            };
        }
    }
}