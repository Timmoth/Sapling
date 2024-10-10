using System.Runtime.CompilerServices;

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
}