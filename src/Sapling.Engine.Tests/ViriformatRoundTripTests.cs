using System.IO;
using FluentAssertions;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;
using Xunit;

namespace Sapling.Engine.Tests;

public class ViriformatRoundTripTests
{
    // Decodes a viriformat move record back into Sapling's move encoding and
    // replays the game with the engine's own Apply, verifying every move is legal.
    private static uint DecodeMove(ushort encoded, BoardStateData board)
    {
        var from = (byte)(encoded & 0x3F);
        var to = (byte)((encoded >> 6) & 0x3F);
        var flags = encoded & 0xC000;
        var promo = (encoded >> 12) & 0x3;

        if (flags == 0x8000)
        {
            // viriformat castle: king-from -> rook-square. Sapling's 960 encoding uses the
            // same target, but standard chess encodes the king destination instead.
            if (!board.Is960)
            {
                to = (byte)(to switch
                {
                    7 => 6,
                    0 => 2,
                    63 => 62,
                    56 => 58,
                    _ => to
                });
            }

            return MoveExtensions.EncodeCastleMove(board.WhiteToMove ? Constants.WhiteKing : Constants.BlackKing, from, to);
        }

        if (flags == 0x4000)
        {
            return board.WhiteToMove
                ? MoveExtensions.EncodeWhiteEnpassantMove(from, (byte)(to % 8))
                : MoveExtensions.EncodeBlackEnpassantMove(from, (byte)(to % 8));
        }

        if (flags == 0xC000)
        {
            var moveType = (byte)(Constants.PawnKnightPromotion + promo);
            var pawn = board.WhiteToMove ? Constants.WhitePawn : Constants.BlackPawn;
            var captured = board.GetPiece(to);
            return captured != Constants.None
                ? MoveExtensions.EncodeCapturePromotionMove(pawn, from, captured, to, moveType)
                : MoveExtensions.EncodePromotionMove(pawn, from, to, moveType);
        }

        var movedPiece = board.GetPiece(from);
        var capturedPiece = board.GetPiece(to);
        return capturedPiece != Constants.None
            ? MoveExtensions.EncodeCaptureMove(movedPiece, from, capturedPiece, to)
            : MoveExtensions.EncodeNormalMove(movedPiece, from, to);
    }

    [Fact]
    public unsafe void GeneratedGame_ReplaysLegallyThroughEngineApply()
    {
        var ttSize = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(16);
        var transpositionTable = MemoryHelpers.Allocate<Transposition>(ttSize);
        var searcher = new Searcher(transpositionTable, ttSize);
        try
        {
            var generator = new DataGenerator();
            var gameState = new GameState(BoardStateExtensions.CreateBoardFromFen(Chess960.Fens[0]));

            Span<(uint move, short eval)> moves = stackalloc (uint move, short eval)[DataGenerator.MaxTurnCount];
            var (result, moveCount) = generator.RunGame(gameState, searcher, moves);

            moveCount.Should().BeGreaterThan(10);

            var startBoard = BoardStateExtensions.CreateBoardFromFen(Chess960.Fens[0]);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                Viriformat.WriteGame(stackalloc byte[Viriformat.MaxGameBytes], writer, ref startBoard, moves, moveCount, result);
            }

            var bytes = stream.ToArray();
            bytes.Length.Should().Be(Viriformat.HeaderSize + (moveCount + 1) * Viriformat.MoveRecordSize);

            // Replay from the written header using the engine's own move application.
            var replay = BoardStateExtensions.CreateBoardFromFen(Chess960.Fens[0]);
            for (var i = 0; i < moveCount; i++)
            {
                var offset = Viriformat.HeaderSize + i * Viriformat.MoveRecordSize;
                var encoded = BitConverter.ToUInt16(bytes, offset);
                encoded.Should().NotBe(0);

                var move = DecodeMove(encoded, replay);
                var legal = replay.WhiteToMove ? replay.PartialApplyWhite(move) : replay.PartialApplyBlack(move);
                legal.Should().BeTrue($"ply {i} move {encoded:X4} must replay legally");
            }

            var terminator = BitConverter.ToUInt32(bytes, Viriformat.HeaderSize + moveCount * Viriformat.MoveRecordSize);
            terminator.Should().Be(0);
        }
        finally
        {
            searcher.Dispose();
            MemoryHelpers.FreeAligned(transpositionTable);
        }
    }

    [Fact]
    public unsafe void GeneratedChess960Game_ReplaysLegallyThroughEngineApply()
    {
        var ttSize = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(16);
        var transpositionTable = MemoryHelpers.Allocate<Transposition>(ttSize);
        var searcher = new Searcher(transpositionTable, ttSize);
        try
        {
            var generator = new DataGenerator();
            var gameState = new GameState(BoardStateExtensions.CreateBoardFromFen(Chess960.Fens[0]));

            Span<(uint move, short eval)> moves = stackalloc (uint move, short eval)[DataGenerator.MaxTurnCount];
            var (result, moveCount) = generator.RunGame(gameState, searcher, moves);

            var startBoard = BoardStateExtensions.CreateBoardFromFen(Chess960.Fens[0]);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                Viriformat.WriteGame(stackalloc byte[Viriformat.MaxGameBytes], writer, ref startBoard, moves, moveCount, result);
            }

            var bytes = stream.ToArray();
            var replay = BoardStateExtensions.CreateBoardFromFen(Chess960.Fens[0]);
            for (var i = 0; i < moveCount; i++)
            {
                var offset = Viriformat.HeaderSize + i * Viriformat.MoveRecordSize;
                var encoded = BitConverter.ToUInt16(bytes, offset);
                var move = DecodeMove(encoded, replay);
                var legal = replay.WhiteToMove ? replay.PartialApplyWhite(move) : replay.PartialApplyBlack(move);
                legal.Should().BeTrue($"960 ply {i} move {encoded:X4} must replay legally");
            }
        }
        finally
        {
            searcher.Dispose();
            MemoryHelpers.FreeAligned(transpositionTable);
        }
    }
}
