using Sapling.Engine;
using Sapling.Engine.Evaluation;

namespace Sapling.Engine.Tests;

// Shared independent scalar NNUE oracle: re-derives feature indexes,
// input buckets and mirroring without production tables, and recomputes accumulators and
// output evaluation from the checked-in weights. Used by the oracle tests and the
// update-coverage tests.
public static unsafe class NnueOracle
{
    public static readonly byte[] OracleBucketLayout =
    {
        0, 1, 2, 3, 3, 2, 1, 0,
        4, 4, 5, 5, 5, 5, 4, 4,
        6, 6, 6, 6, 6, 6, 6, 6,
        6, 6, 6, 6, 6, 6, 6, 6,
        7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7,
    };

    public static readonly byte[] PieceTypes =
    {
        Constants.BlackPawn, Constants.BlackKnight, Constants.BlackBishop, Constants.BlackRook,
        Constants.BlackQueen, Constants.BlackKing, Constants.WhitePawn, Constants.WhiteKnight,
        Constants.WhiteBishop, Constants.WhiteRook, Constants.WhiteQueen, Constants.WhiteKing,
    };

    public static int RawFeatureIndex(byte piece, int square, int mirror, bool whiteView)
    {
        var white = (piece & 1) ^ 1;
        var type = (piece >> 1) - white;
        return whiteView
            ? (white ^ 1) * 384 + type * 64 + (square ^ mirror)
            : white * 384 + type * 64 + (square ^ mirror ^ 0x38);
    }

    public static int OracleInputBucket(BoardStateData* board, bool whiteView)
    {
        return whiteView
            ? OracleBucketLayout[board->WhiteKingSquare]
            : OracleBucketLayout[board->BlackKingSquare ^ 0x38];
    }

    public static int OracleMirror(BoardStateData* board, bool whiteView)
    {
        var kingSquare = whiteView ? board->WhiteKingSquare : board->BlackKingSquare;
        return (kingSquare & 7) >= 4 ? 7 : 0;
    }

    public static short[] OracleAccumulator(BoardStateData* board, bool whiteView)
    {
        var bucket = OracleInputBucket(board, whiteView);
        var mirror = OracleMirror(board, whiteView);
        var weights = (short*)NnueWeights.FeatureWeights + (nuint)bucket * (NnueWeights.InputSize * NnueWeights.Layer1Size);
        var biases = (short*)NnueWeights.FeatureBiases;

        var acc = new short[NnueWeights.Layer1Size];
        for (var i = 0; i < acc.Length; i++)
        {
            acc[i] = biases[i];
        }

        foreach (var piece in PieceTypes)
        {
            var bitboard = board->Occupancy[piece];
            while (bitboard != 0)
            {
                var square = System.Numerics.BitOperations.TrailingZeroCount(bitboard);
                bitboard &= bitboard - 1;
                var raw = RawFeatureIndex(piece, square, mirror, whiteView);
                var feature = weights + (nuint)raw * NnueWeights.Layer1Size;
                for (var i = 0; i < acc.Length; i++)
                {
                    acc[i] = unchecked((short)(acc[i] + feature[i]));
                }
            }
        }

        return acc;
    }

    public static short[] OracleApply(short[] source, BoardStateData* board,
        params (byte piece, int square, int sign, bool whiteView)[] updates)
    {
        var acc = (short[])source.Clone();
        foreach (var (piece, square, sign, whiteView) in updates)
        {
            var bucket = OracleInputBucket(board, whiteView);
            var mirror = OracleMirror(board, whiteView);
            var weights = (short*)NnueWeights.FeatureWeights + (nuint)bucket * (NnueWeights.InputSize * NnueWeights.Layer1Size);
            var raw = RawFeatureIndex(piece, square, mirror, whiteView);
            var feature = weights + (nuint)raw * NnueWeights.Layer1Size;
            for (var i = 0; i < acc.Length; i++)
            {
                acc[i] = unchecked((short)(acc[i] + sign * feature[i]));
            }
        }

        return acc;
    }

    public static int OracleEval(BoardStateData* board, short[] whiteAcc, short[] blackAcc)
    {
        const int outputBuckets = NnueWeights.OutputBuckets;
        var bucketDivisor = (32 + outputBuckets - 1) / outputBuckets;
        var bucket = (board->PieceCount - 2) / bucketDivisor;

        var us = board->WhiteToMove ? whiteAcc : blackAcc;
        var them = board->WhiteToMove ? blackAcc : whiteAcc;
        var outputWeights = (short*)NnueWeights.OutputWeights + (nuint)bucket * 2 * NnueWeights.Layer1Size;

        var sum = 0;
        for (var i = 0; i < NnueWeights.Layer1Size; i++)
        {
            var u = Math.Clamp((int)us[i], 0, 255);
            var t = Math.Clamp((int)them[i], 0, 255);
            sum = unchecked(sum + unchecked((short)(u * outputWeights[i])) * u
                + unchecked((short)(t * outputWeights[NnueWeights.Layer1Size + i])) * t);
        }

        return unchecked((sum / 255 + NnueWeights.OutputBiases[bucket]) * 400 / (255 * 64));
    }

    public static int OracleEvalFromScratch(BoardStateData* board)
    {
        return OracleEval(board, OracleAccumulator(board, true), OracleAccumulator(board, false));
    }
}
