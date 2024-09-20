using System.Runtime.CompilerServices;
using Sapling.Engine.Evaluation;

namespace Sapling.Engine.MoveGen;

public static class MoveScoring
{
    private const short MaxMateDepth = 1000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short EvaluateFinalPosition(int ply, bool isInCheck)
    {
        if (isInCheck)
        {
            return (short)(-Constants.ImmediateMateScore + ply);
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMateScore(int score)
    {
        if (score == int.MinValue)
        {
            return false;
        }

        return Math.Abs(score) > Constants.ImmediateMateScore - MaxMateDepth;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMateDistance(int score)
    {
        return (Constants.ImmediateMateScore - Math.Abs(score) + 1) / 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int ScoreMove(this ref BoardStateData board, int* history, ulong* occupancyBitBoards,
        short* captures,
        uint move,
        uint killerA,
        uint killerB,
        uint bestMove,
        uint counterMove)
    {
        // Order:
        // Best move - 100m
        // Promotion capture - 10m + material gain + 600k
        // Good capture - 10m + material gain
        // Promotion - 600k
        // KillerA - 500k
        // KillerB - 250k
        // Counter - 65k
        // History - 0-8k
        // Bad capture - 6k

        if (bestMove == move)
        {
            return 100_000_000;
        }

        // Prioritize high value capture with low value piece
        if (move.IsCapture())
        {
            if (move.IsEnPassant())
            {
                return Constants.WinningCaptureBias;
            }

            var captureDelta = board.StaticExchangeEvaluation(occupancyBitBoards, captures, move);

            return (captureDelta >= 0 ? Constants.WinningCaptureBias : Constants.LosingCaptureBias) +
                   captureDelta + (move.IsPromotion() ? Constants.PromoteBias : 0);
        }

        if (move.IsPromotion())
        {
            return Constants.PromoteBias;
        }

        if (killerA == move)
        {
            return Constants.KillerABias;
        }

        if (killerB == move)
        {
            return Constants.KillerBBias;
        }

        if (counterMove == move)
        {
            return Constants.CounterMoveBias;
        }

        return history[move.GetMovedPiece() * 64 + move.GetToSquare()];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int ScoreMoveQuiescence(this ref BoardStateData board, ulong* occupancyBitBoards, short* captures,
        uint move,
        uint bestMove)
    {
        // Order:
        // Best move - 100m
        // Promotion capture - 10m + material gain + 600k
        // Good capture - 10m + material gain
        // Promotion - 600k
        // Bad capture - 6k

        if (bestMove == move)
        {
            return 100_000_000;
        }

        // Prioritize high value capture with low value piece
        if (move.IsCapture())
        {
            if (move.IsEnPassant())
            {
                return Constants.WinningCaptureBias;
            }

            var captureDelta = board.StaticExchangeEvaluation(occupancyBitBoards, captures, move);

            return (captureDelta >= 0 ? Constants.WinningCaptureBias : Constants.LosingCaptureBias) +
                   captureDelta + (move.IsPromotion() ? Constants.PromoteBias : 0);
        }

        if (move.IsPromotion())
        {
            return Constants.PromoteBias;
        }

        return 0;
    }
}