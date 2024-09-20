using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Search;
#if AVX512
using AvxIntrinsics = System.Runtime.Intrinsics.X86.Avx512BW;
using VectorType = System.Runtime.Intrinsics.Vector512;
using VectorInt = System.Runtime.Intrinsics.Vector512<int>;
using VectorShort = System.Runtime.Intrinsics.Vector512<short>;
#else
using AvxIntrinsics = Avx2;
using VectorType = Vector256;
using VectorInt = Vector256<int>;
using VectorShort = Vector256<short>;
#endif

public partial class Searcher
{
    public unsafe int QuiescenceSearch(ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc, ulong* hashHistory, int depthFromRoot, int alpha, int beta)
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
            return board.Evaluate(whiteAcc, blackAcc);
        }

        var pvIndex = PVTable.Indexes[depthFromRoot];
        var nextPvIndex = PVTable.Indexes[depthFromRoot + 1];
        _pVTable[pvIndex] = 0;

        if (board.InsufficientMatingMaterial())
        {
            // Detect draw by Fifty move counter or repetition
            return 0;
        }

        if (alpha < 0 && board.HasRepetition(hashHistory, depthFromRoot))
        {
            alpha = 0;
            if (alpha >= beta)
                return alpha;
        }

        var ttProbeResult =
            TranspositionTableExtensions.Get(_transpositionTable, TtMask, board.Hash, 0, depthFromRoot, alpha, beta);
        if (ttProbeResult.Evaluation != TranspositionTableExtensions.NoHashEntry)
        {
            // Transposition table hit
            return ttProbeResult.Evaluation;
        }

        var inCheck = board.InCheck;
        if (!inCheck)
        {
            // Evaluate current position
            var val = board.Evaluate(whiteAcc, blackAcc);
            if (val >= beta)
            {
                // Beta cut off
                return val;
            }

            alpha = int.Max(alpha, val);
        }

        // Get all capturing moves
        var moves = stackalloc uint[218];
        var psuedoMoveCount = board.GeneratePseudoLegalMoves(moves, !inCheck);

        if (psuedoMoveCount == 0)
        {
            if (inCheck)
            {
                // No move could be played, either stalemate or checkmate
                var finalEval = MoveScoring.EvaluateFinalPosition(depthFromRoot, inCheck);

                // Cache in transposition table
                TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, 0, depthFromRoot, finalEval,
                    TranspositionTableFlag.Exact);
                return finalEval;
            }

            TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, 0, depthFromRoot, alpha,
                TranspositionTableFlag.Alpha,
                default);
            return alpha;
        }

        Span<int> scores = stackalloc int[psuedoMoveCount];

        var occupancyBitBoards = stackalloc ulong[8]
        {
            board.Occupancy[Constants.WhitePieces],
            board.Occupancy[Constants.BlackPieces],
            board.Occupancy[Constants.BlackPawn] | board.Occupancy[Constants.WhitePawn],
            board.Occupancy[Constants.BlackKnight] | board.Occupancy[Constants.WhiteKnight],
            board.Occupancy[Constants.BlackBishop] | board.Occupancy[Constants.WhiteBishop],
            board.Occupancy[Constants.BlackRook] | board.Occupancy[Constants.WhiteRook],
            board.Occupancy[Constants.BlackQueen] | board.Occupancy[Constants.WhiteQueen],
            board.Occupancy[Constants.BlackKing] | board.Occupancy[Constants.WhiteKing]
        };

        var captures = stackalloc short[board.PieceCount];

        for (var i = 0; i < psuedoMoveCount; ++i)
        {
            scores[i] = board.ScoreMoveQuiescence(occupancyBitBoards, captures, moves[i], ttProbeResult.BestMove);
        }

        var oldEnpassant = board.EnPassantFile;
        var prevInCheck = board.InCheck;

        var prevCastleRights = board.CastleRights;
        var evaluationBound = TranspositionTableFlag.Alpha;

        uint bestMove = default;
        var hasValidMove = false;

        var whiteAccPtr = stackalloc VectorShort[NnueEvaluator.AccumulatorSize];
        var blackAccPtr = stackalloc VectorShort[NnueEvaluator.AccumulatorSize];

        BoardStateData copy = default;

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
            
            board.CloneTo(ref copy);

            var m = moves[moveIndex];

            if (!copy.PartialApply(m))
            {
                // illegal move
                continue;
            }

            copy.UpdateCheckStatus();

            hasValidMove = true;

            if (!prevInCheck && !copy.InCheck && scores[moveIndex] < Constants.LosingCaptureBias)
            {
                //skip playing bad captures when not in check
                continue;
            }

            NnueEvaluator.SimdCopy(whiteAccPtr, whiteAcc);
            NnueEvaluator.SimdCopy(blackAccPtr, blackAcc);

            copy.FinishApply(whiteAccPtr, blackAccPtr, hashHistory, m, oldEnpassant, prevCastleRights);

            Sse.Prefetch0(_transpositionTable + (copy.Hash & TtMask));

            var val = -QuiescenceSearch(ref copy, whiteAccPtr, blackAccPtr, hashHistory, depthFromRoot + 1, -beta, -alpha);

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
                TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, 0, depthFromRoot, val,
                    TranspositionTableFlag.Beta, bestMove);
                // Beta cut off
                return val;
            }

            if (!_searchCancelled)
            {
                // update pv table
                _pVTable[pvIndex] = m;
                ShiftPvMoves(pvIndex + 1, nextPvIndex, Constants.MaxSearchDepth - depthFromRoot - 1);
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
            TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, 0, depthFromRoot, finalEval,
                TranspositionTableFlag.Exact);
            return finalEval;
        }

        // Cache in transposition table
        TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, 0, depthFromRoot, alpha,
            evaluationBound,
            bestMove);

        return alpha;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void ShiftPvMoves(int target, int source, int moveCountToCopy)
    {
        if (_pVTable[source] == 0)
        {
            NativeMemory.Clear(_pVTable + target, _pvTableBytes - (nuint)target * sizeof(uint));
            return;
        }

        NativeMemory.Copy(_pVTable + source, _pVTable + target, (nuint)moveCountToCopy * sizeof(uint));
    }
}