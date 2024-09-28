using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Search;

public partial class Searcher
{
    public unsafe int QuiescenceSearch(int depthFromRoot, int alpha, int beta)
    {
        NodesVisited++;

        if (_searchCancelled)
        {
            // Search was cancelled
            return 0;
        }
 
        if (depthFromRoot >= Constants.MaxSearchDepth)
        {
            // Reached max depth
            return NnueEvaluator.Evaluate(SearchStack, BucketCache, depthFromRoot);
        }

        ref var boardState = ref SearchStack[depthFromRoot + 1];
        ref var pAccumulator = ref SearchStack[depthFromRoot].AccumulatorState;
        ref var pboard = ref SearchStack[depthFromRoot].Data;

        if (pboard.InsufficientMatingMaterial())
        {
            // Detect draw by Fifty move counter or repetition
            return 0;
        }

        if (alpha < 0 && pboard.HasRepetition(MoveStack, depthFromRoot))
        {
            alpha = 0;
            if (alpha >= beta)
                return alpha;
        }

        var ttProbeResult =
            TranspositionTableExtensions.Get(Transpositions, TtMask, pboard.Hash, 0, depthFromRoot, alpha, beta);
        if (ttProbeResult.Evaluation != TranspositionTableExtensions.NoHashEntry)
        {
            // Transposition table hit
            return ttProbeResult.Evaluation;
        }

        var inCheck = pboard.InCheck;
        if (!inCheck)
        {
            // Evaluate current position
            var val = NnueEvaluator.Evaluate(SearchStack, BucketCache, depthFromRoot);
            if (val >= beta)
            {
                // Beta cut off
                return val;
            }

            alpha = int.Max(alpha, val);
        }

        // Get all capturing moves
        var moves = stackalloc uint[218];
        var psuedoMoveCount = pboard.GeneratePseudoLegalMoves(moves, !inCheck);

        if (psuedoMoveCount == 0)
        {
            if (inCheck)
            {
                // No move could be played, either stalemate or checkmate
                var finalEval = MoveScoring.EvaluateFinalPosition(depthFromRoot, inCheck);

                // Cache in transposition table
                TranspositionTableExtensions.Set(Transpositions, TtMask, pboard.Hash, 0, depthFromRoot, finalEval,
                    TranspositionTableFlag.Exact);
                return finalEval;
            }

            TranspositionTableExtensions.Set(Transpositions, TtMask, pboard.Hash, 0, depthFromRoot, alpha,
                TranspositionTableFlag.Alpha,
                default);
            return alpha;
        }

        Span<int> scores = stackalloc int[psuedoMoveCount];

        var occupancyBitBoards = stackalloc ulong[8]
        {
            pboard.Occupancy[Constants.WhitePieces],
            pboard.Occupancy[Constants.BlackPieces],
            pboard.Occupancy[Constants.BlackPawn] | pboard.Occupancy[Constants.WhitePawn],
            pboard.Occupancy[Constants.BlackKnight] | pboard.Occupancy[Constants.WhiteKnight],
            pboard.Occupancy[Constants.BlackBishop] | pboard.Occupancy[Constants.WhiteBishop],
            pboard.Occupancy[Constants.BlackRook] | pboard.Occupancy[Constants.WhiteRook],
            pboard.Occupancy[Constants.BlackQueen] | pboard.Occupancy[Constants.WhiteQueen],
            pboard.Occupancy[Constants.BlackKing] | pboard.Occupancy[Constants.WhiteKing]
        };

        var captures = stackalloc short[pboard.PieceCount];

        for (var i = 0; i < psuedoMoveCount; ++i)
        {
            scores[i] = pboard.ScoreMoveQuiescence(occupancyBitBoards, captures, moves[i], ttProbeResult.BestMove);
        }

        var evaluationBound = TranspositionTableFlag.Alpha;

        uint bestMove = default;
        var hasValidMove = false;
        ref var board = ref boardState.Data;

        for (var moveIndex = 0; moveIndex < psuedoMoveCount; ++moveIndex)
        {
            // Incremental move sorting
            for (var j = moveIndex + 1; j < psuedoMoveCount; j++)
            {
                if (scores[j] > scores[moveIndex])
                {
                    (scores[moveIndex], scores[j], moves[moveIndex], moves[j]) =
                        (scores[j], scores[moveIndex], moves[j], moves[moveIndex]);
                }
            }

            pboard.CloneTo(ref board);

            var m = moves[moveIndex];

            if (!board.PartialApply(m))
            {
                // illegal move
                continue;
            }

            board.UpdateCheckStatus();

            hasValidMove = true;

            if (!pboard.InCheck && !board.InCheck && scores[moveIndex] < Constants.LosingCaptureBias)
            {
                //skip playing bad captures when not in check
                continue;
            }

            boardState.AccumulatorState.UpdateToParent(ref pAccumulator, ref board);
            board.FinishApply(ref boardState.AccumulatorState, m, pboard.EnPassantFile, pboard.CastleRights);
            MoveStack[board.TurnCount - 1] = board.Hash;

            Sse.Prefetch0(Transpositions + (board.Hash & TtMask));

            var val = -QuiescenceSearch(depthFromRoot + 1, -beta, -alpha);

            if (_searchCancelled)
            {
                // Search was cancelled
                return 0;
            }

            if (val <= alpha)
            {
                // Move was not better then alpha, continue searching
                continue;
            }

            evaluationBound = TranspositionTableFlag.Exact;
            bestMove = m;
            alpha = val;

            if (val >= beta)
            {
                // Cache in transposition table
                TranspositionTableExtensions.Set(Transpositions, TtMask, pboard.Hash, 0, depthFromRoot, val,
                    TranspositionTableFlag.Beta, bestMove);
                // Beta cut off
                return val;
            }
        }

        if (_searchCancelled)
        {
            // Search was cancelled
            return 0;
        }

        if (!hasValidMove && inCheck)
        {
            // No move could be played, either stalemate or checkmate
            var finalEval = MoveScoring.EvaluateFinalPosition(depthFromRoot, inCheck);

            // Cache in transposition table
            TranspositionTableExtensions.Set(Transpositions, TtMask, pboard.Hash, 0, depthFromRoot, finalEval,
                TranspositionTableFlag.Exact);
            return finalEval;
        }

        // Cache in transposition table
        TranspositionTableExtensions.Set(Transpositions, TtMask, pboard.Hash, 0, depthFromRoot, alpha,
            evaluationBound,
            bestMove);

        return alpha;
    }

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    //private unsafe void ShiftPvMoves(int target, int source, int moveCountToCopy)
    //{
    //    if (_pVTable[source] == 0)
    //    {
    //        NativeMemory.Clear(_pVTable + target, _pvTableBytes - (nuint)target * sizeof(uint));
    //        return;
    //    }

    //    NativeMemory.Copy(_pVTable + source, _pVTable + target, (nuint)moveCountToCopy * sizeof(uint));
    //}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void ShiftPvMoves(int target, int source, int moveCountToCopy)
    {
        // Check if the source position is zero
        if (_pVTable[source] == 0)
        {
            // Calculate the number of bytes to clear
            var bytesToClear = _pvTableBytes - (nuint)target * sizeof(uint);
            NativeMemory.Clear(_pVTable + target, bytesToClear);
            return;
        }
        
        Unsafe.CopyBlock(_pVTable + target, _pVTable + source, (uint)(moveCountToCopy * sizeof(uint)));
    }
}