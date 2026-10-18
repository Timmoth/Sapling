using System.Buffers.Binary;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.DataGen;

// Writer for the viriformat v2 binpack format.
//
// A file is a flat sequence of games. Each game is:
//   - a 32-byte packed header for the initial position,
//   - one 4-byte record per move: u16 move + i16 eval (White-relative), little endian,
//   - a 4-byte all-zero null terminator.
//
// Header layout (little endian):
//   @0  u64 occupancy (square order a8=0 .. h1=63)
//   @8  16 bytes: 4-bit piece codes in occupancy order: piece_type | colour << 3,
//       where a rook that still holds a castling right on its home square uses
//       code 6 (UNMOVED_ROOK) instead of 3 - this is how castling rights are stored.
//   @24 u8  (sideToMove << 7) | epSquare (64 when none)
//   @25 u8  halfmove clock
//   @26 u16 fullmove number
//   @28 i16 eval (unused in headers, 0)
//   @30 u8  wdl: 0 = black win, 1 = draw, 2 = white win
//   @31 u8  extra (0)
//
// Move encoding (u16): from(6) | to(6) << 6 | promo(2) << 12 | flags:
//   promotion: 0b11 << 14 | (piece - 1) << 12 with N=1,B=2,R=3,Q=4
//   en passant: 1 << 14, to = destination square (the ep square)
//   castle: 1 << 15, from = king square, to = rook square (chess960 style)
//   quiet / capture: no flags
public static class Viriformat
{
    public const int HeaderSize = 32;
    public const int MoveRecordSize = 4;

    public const byte WdlBlackWin = 0;
    public const byte WdlDraw = 1;
    public const byte WdlWhiteWin = 2;

    private const byte UnmovedRookCode = 6;

    private const ushort PromotionFlag = 0b1100_0000_0000_0000;
    private const ushort EnPassantFlag = 0b0100_0000_0000_0000;
    private const ushort CastleFlag = 0b1000_0000_0000_0000;

    // viriformat squares are a1=0..h8=63, identical to Sapling's ordering.
    private static int ToViriSquare(int square) => square;

    // Sapling piece codes (Constants) -> viriformat piece code (piece_type | colour << 3).
    private static byte ToViriPieceCode(byte piece) => (byte)((piece - 1) / 2 + (piece % 2) * 8);

    public static ushort EncodeMove(uint move, bool is960)
    {
        var from = (ushort)(ToViriSquare(move.GetFromSquare()) & 0x3F);
        var toSquare = move.GetToSquare();
        var moveType = move.GetMoveType();

        if (moveType == Constants.Castle)
        {
            // viriformat encodes castling as king-from -> rook-square, which matches Sapling's
            // chess960 encoding. Standard-chess Sapling moves encode the king destination,
            // so remap those to the matching rook square.
            if (!is960)
            {
                toSquare = toSquare switch
                {
                    6 => 7,
                    2 => 0,
                    62 => 63,
                    58 => 56,
                    _ => toSquare
                };
            }

            return (ushort)(from | (ToViriSquare(toSquare) << 6) | CastleFlag);
        }

        var to = (ushort)(ToViriSquare(toSquare) & 0x3F);

        if (moveType == Constants.EnPassant)
        {
            return (ushort)(from | (to << 6) | EnPassantFlag);
        }

        if (moveType >= Constants.PawnKnightPromotion)
        {
            // Sapling: N=4, B=5, R=6, Q=7 -> viriformat promo bits: N=0, B=1, R=2, Q=3.
            return (ushort)(from | (to << 6) | ((moveType - Constants.PawnKnightPromotion) << 12) | PromotionFlag);
        }

        return (ushort)(from | (to << 6));
    }

    private static bool IsCastlingRookSquare(ref BoardStateData board, int square)
    {
        var whiteKingSide = board.Is960 ? board.WhiteKingSideTargetSquare : (byte)7;
        var whiteQueenSide = board.Is960 ? board.WhiteQueenSideTargetSquare : (byte)0;
        var blackKingSide = board.Is960 ? board.BlackKingSideTargetSquare : (byte)63;
        var blackQueenSide = board.Is960 ? board.BlackQueenSideTargetSquare : (byte)56;

        return (board.CastleRights & CastleRights.WhiteKingSide) != 0 && square == whiteKingSide ||
               (board.CastleRights & CastleRights.WhiteQueenSide) != 0 && square == whiteQueenSide ||
               (board.CastleRights & CastleRights.BlackKingSide) != 0 && square == blackKingSide ||
               (board.CastleRights & CastleRights.BlackQueenSide) != 0 && square == blackQueenSide;
    }

    public static unsafe void WriteHeader(Span<byte> header, ref BoardStateData start, byte wdl)
    {
        header.Clear();

        var occupancy = start.Occupancy[Constants.Occupancy];
        BinaryPrimitives.WriteUInt64LittleEndian(header, occupancy);

        var slot = 0;
        for (var square = 0; square < 64; square++)
        {
            if ((occupancy & (1UL << square)) == 0)
            {
                continue;
            }

            var piece = start.GetPiece(square);
            var code = ToViriPieceCode(piece);

            if (piece is Constants.WhiteRook or Constants.BlackRook && IsCastlingRookSquare(ref start, square))
            {
                code = (byte)(UnmovedRookCode | (piece % 2) * 8);
            }

            header[8 + (slot >> 1)] |= (byte)(code << ((slot & 1) * 4));
            slot++;
        }

        // Bit 7 of the stm/ep byte is the side to move (0 = white, 1 = black);
        // datagen starts never have an en passant square (64 = none).
        header[24] = (byte)(64 | (start.WhiteToMove ? 0 : 0x80));
        header[25] = start.HalfMoveClock;
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[28..], 0);
        header[30] = wdl;
        header[31] = 0;
    }

    // Max bytes for a whole game: header + one record per ply + null terminator.
    public const int MaxGameBytes = HeaderSize + (DataGenerator.MaxTurnCount + 1) * MoveRecordSize;

    // Serializes the entire game into a single buffer and writes it with one Write call.
    // Combined with a per-game Flush(true) by the caller, a crash can never leave a
    // partial game in the file, which would make everything after it unparseable.
    public static unsafe void WriteGame(Span<byte> scratch, BinaryWriter writer, ref BoardStateData start,
        ReadOnlySpan<(uint move, short eval)> moves, int moveCount, byte wdl)
    {
        var game = scratch.Slice(0, HeaderSize + (moveCount + 1) * MoveRecordSize);
        var header = game.Slice(0, HeaderSize);
        WriteHeader(header, ref start, wdl);

        var is960 = start.Is960;
        var whiteToMove = start.WhiteToMove;
        for (var i = 0; i < moveCount; i++)
        {
            var record = game.Slice(HeaderSize + i * MoveRecordSize, MoveRecordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(record, EncodeMove(moves[i].move, is960));
            var score = whiteToMove ? moves[i].eval : (short)Math.Min(-(int)moves[i].eval, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(record[2..], score);
            whiteToMove = !whiteToMove;
        }

        game.Slice(HeaderSize + moveCount * MoveRecordSize, MoveRecordSize).Clear();
        writer.Write(game);
    }
}
