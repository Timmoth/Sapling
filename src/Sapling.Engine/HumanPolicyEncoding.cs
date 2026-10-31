using Sapling.Engine.MoveGen;

namespace Sapling.Engine;

/// <summary>Schema v1 tensors shared with the offline human-policy trainer.</summary>
public static unsafe class HumanPolicyEncoding
{
    public const int SchemaVersion = 1;
    public const int BoardChannels = 18;
    public const int SquaresPerBoard = 64;
    public const int MoveFeatures = 134;
    public const int MinRating = 500;
    public const int MaxRating = 2200;

    private static readonly byte[] PieceChannels =
    [
        Constants.WhitePawn, Constants.WhiteKnight, Constants.WhiteBishop,
        Constants.WhiteRook, Constants.WhiteQueen, Constants.WhiteKing,
        Constants.BlackPawn, Constants.BlackKnight, Constants.BlackBishop,
        Constants.BlackRook, Constants.BlackQueen, Constants.BlackKing
    ];

    /// <summary>Returns a flattened [18,8,8] board tensor; index = channel*64 + square.</summary>
    public static float[] EncodeBoard(ref BoardStateData board)
    {
        var encoded = new float[BoardChannels * SquaresPerBoard];
        fixed (ulong* occupancy = board.Occupancy)
        {
            for (var channel = 0; channel < PieceChannels.Length; channel++)
            {
                var pieces = occupancy[PieceChannels[channel]];
                while (pieces != 0)
                {
                    var square = System.Numerics.BitOperations.TrailingZeroCount(pieces);
                    encoded[channel * SquaresPerBoard + square] = 1f;
                    pieces &= pieces - 1;
                }
            }
        }

        if (board.WhiteToMove)
            Array.Fill(encoded, 1f, 12 * SquaresPerBoard, SquaresPerBoard);

        SetFlagPlane(encoded, 13, board.CastleRights.HasFlag(CastleRights.WhiteKingSide));
        SetFlagPlane(encoded, 14, board.CastleRights.HasFlag(CastleRights.WhiteQueenSide));
        SetFlagPlane(encoded, 15, board.CastleRights.HasFlag(CastleRights.BlackKingSide));
        SetFlagPlane(encoded, 16, board.CastleRights.HasFlag(CastleRights.BlackQueenSide));

        if (board.EnPassantFile < 8)
        {
            // FEN semantics: after black's double push the target is on rank 6;
            // after white's double push it is on rank 3, whether capturable or not.
            var rank = board.WhiteToMove ? 5 : 2;
            encoded[17 * SquaresPerBoard + rank * 8 + board.EnPassantFile] = 1f;
        }

        return encoded;
    }

    /// <summary>
    /// Encodes a legal move as from[64], to[64], promotion[Q,R,B,N], normalized
    /// mover-perspective root score, and normalized gap from the best root score.
    /// </summary>
    public static float[] EncodeMove(uint move, int rootScore, int bestRootScore)
    {
        var encoded = new float[MoveFeatures];
        EncodeMove(move, rootScore, bestRootScore, encoded);
        return encoded;
    }

    public static void EncodeMove(uint move, int rootScore, int bestRootScore, Span<float> encoded)
    {
        if (encoded.Length != MoveFeatures)
            throw new ArgumentException($"Move features must have length {MoveFeatures}.", nameof(encoded));
        encoded.Clear();
        encoded[move.GetFromSquare()] = 1f;
        encoded[64 + move.GetToSquare()] = 1f;

        var promotionIndex = move.IsQueenPromotion() ? 0
            : move.IsRookPromotion() ? 1
            : move.IsBishopPromotion() ? 2
            : move.IsKnightPromotion() ? 3
            : -1;
        if (promotionIndex >= 0)
            encoded[128 + promotionIndex] = 1f;

        encoded[132] = (float)Math.Tanh(rootScore / 1000.0);
        encoded[133] = (float)Math.Tanh(Math.Max(0, bestRootScore - rootScore) / 1000.0);
    }

    public static float EncodeRating(int rating)
    {
        if (rating is < MinRating or > MaxRating)
            throw new ArgumentOutOfRangeException(nameof(rating), rating,
                $"Rating must be in [{MinRating},{MaxRating}].");
        return (rating - 1500) / 700f;
    }

    private static void SetFlagPlane(float[] board, int channel, bool value)
    {
        if (value)
            Array.Fill(board, 1f, channel * SquaresPerBoard, SquaresPerBoard);
    }
}
