using System.Runtime.CompilerServices;

namespace iBlunder.Engine.Transpositions;

public static class TranspositionTableExtensions
{
    public const int NoHashEntry = 25_000;

    public const int
        PositiveCheckmateDetectionLimit =
            27_000;

    public const int
        NegativeCheckmateDetectionLimit =
            -27_000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int RecalculateMateScores(int score, int ply)
    {
        return score
               + score switch
               {
                   > PositiveCheckmateDetectionLimit => -ply,
                   < NegativeCheckmateDetectionLimit => ply,
                   _ => 0
               };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe (int Evaluation, uint BestMove, TranspositionTableFlag NodeType) Get(Transposition* tt,
        ulong ttMask, ulong hash, int depth, int ply, int alpha, int beta)
    {
        ref var entry = ref tt[hash & ttMask];

        if (hash != entry.FullHash)
        {
            return (NoHashEntry, default, default);
        }

        var eval = NoHashEntry;

        if (entry.Depth < depth)
        {
            return (eval, entry.Move, entry.Flag);
        }

        // We want to translate the checkmate position relative to the saved node to our root position from which we're searching
        // If the recorded score is a checkmate in 3 and we are at depth 5, we want to read checkmate in 8
        var score = RecalculateMateScores(entry.Evaluation, ply);

        eval = entry.Flag switch
        {
            TranspositionTableFlag.Exact => score,
            TranspositionTableFlag.Alpha when score <= alpha => alpha,
            TranspositionTableFlag.Beta when score >= beta => beta,
            _ => NoHashEntry
        };

        return (eval, entry.Move, entry.Flag);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Set(Transposition* tt, ulong ttMask, ulong hash, byte depth, int ply,
        int eval, TranspositionTableFlag nodeType, uint move = default)
    {
        ref var entry = ref tt[hash & ttMask];

        var shouldReplace =
            entry.FullHash == 0 // No actual entry
            || hash != entry.FullHash // Different key: collision
            || nodeType == TranspositionTableFlag.Exact // Entering PV data
            || depth >= entry.Depth; // Higher depth

        if (!shouldReplace)
        {
            return;
        }

        entry.FullHash = hash;
        entry.Evaluation = RecalculateMateScores(eval, -ply);
        entry.Depth = depth;
        entry.Flag = nodeType;
        entry.Move = move != 0 ? move : entry.Move; //Don't clear TT move if no best move is provided: keep old one
    }
}