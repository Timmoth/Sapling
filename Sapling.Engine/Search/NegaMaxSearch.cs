using System.Runtime.Intrinsics.X86;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Search;

public partial class Searcher
{
    public unsafe int
        NegaMaxSearch(int depthFromRoot, int depth,
            int alpha, int beta, bool wasReducedMove, uint prevMove = default)
    {
        NodesVisited++;

        if (_searchCancelled)
        {
            // Search was cancelled, return
            return 0;
        }

        if (depthFromRoot >= Constants.MaxSearchDepth)
        {
            // Max depth reached, return evaluation
            return NnueEvaluator.Evaluate(SearchStack, BucketCache, depthFromRoot);
        }

        ref var pboard = ref SearchStack[depthFromRoot].Data;
        ref var accumulator = ref SearchStack[depthFromRoot + 1].AccumulatorState;
        ref var board = ref SearchStack[depthFromRoot + 1].Data;

        var pvIndex = PVTable.Indexes[depthFromRoot];
        var nextPvIndex = PVTable.Indexes[depthFromRoot + 1];
        _pVTable[pvIndex] = 0;

        var pvNode = beta - alpha > 1;
        var parentInCheck = pboard.InCheck;
        var pHash = pboard.Hash;

        var canPrune = false;
        uint transpositionBestMove = default;
        TranspositionTableFlag transpositionType = default;

        if (depthFromRoot > 0)
        {
            var mateScore = Constants.ImmediateMateScore - depthFromRoot;
            alpha = Math.Max(alpha, -mateScore);
            beta = Math.Min(beta, mateScore);
            if (alpha >= beta)
            {
                // Faster mating sequence found
                return alpha;
            }

            if (pboard.HalfMoveClock >= 100 || pboard.InsufficientMatingMaterial())
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

            (var transpositionEvaluation, transpositionBestMove, transpositionType) =
                TranspositionTableExtensions.Get(Transpositions, TtMask, pHash, depth, depthFromRoot, alpha,
                    beta);

            if (!pvNode && transpositionEvaluation != TranspositionTableExtensions.NoHashEntry)
            {
                // Transposition table hit
                return transpositionEvaluation;
            }

            if (parentInCheck)
            {
                // Extend searches when in check
                depth++;
            }
            else if (!pvNode)
            {
                var staticEval = NnueEvaluator.Evaluate(SearchStack, BucketCache, depthFromRoot);

                // Reverse futility pruning
                var margin = depth * SpsaOptions.ReverseFutilityPruningMargin;
                if (depth <= SpsaOptions.ReverseFutilityPruningDepth && staticEval >= beta + margin)
                {
                    return staticEval - margin;
                }

                // Null move pruning
                if (staticEval >= beta &&
                    !wasReducedMove &&
                    (transpositionType != TranspositionTableFlag.Alpha || transpositionEvaluation >= beta) &&
                    depth > SpsaOptions.NullMovePruningDepth && pboard.HasMajorPieces())
                {
                    var reduction = Math.Max(0, (depth - SpsaOptions.NullMovePruningReductionA) / SpsaOptions.NullMovePruningReductionB + SpsaOptions.NullMovePruningReductionC);

                    pboard.CloneTo(ref board);
                    board.ApplyNullMove();

                    accumulator.UpdateTo(ref board);

                    var nullMoveScore = -NegaMaxSearch(depthFromRoot + 1,
                        Math.Max(depth - reduction - 1, 0), -beta,
                        -beta + 1, true, prevMove);

                    if (nullMoveScore >= beta)
                    {
                        // Beta cutoff

                        // Cache in Transposition table
                        TranspositionTableExtensions.Set(Transpositions, TtMask, pHash, (byte)depth,
                            depthFromRoot,
                            beta, TranspositionTableFlag.Beta);
                        return beta;
                    }
                }

                // Razoring
                if (depth is > 0 and <= 3)
                {
                    var score = staticEval + SpsaOptions.RazorMarginA;
                    if (depth == 1 && score < beta)
                    {
                        NodesVisited--;
                        var qScore = QuiescenceSearch(depthFromRoot, alpha, beta);

                        return int.Max(qScore, score);
                    }

                    score = staticEval + SpsaOptions.RazorMarginB;
                    if (score < beta)
                    {
                        NodesVisited--;
                        var qScore = QuiescenceSearch(depthFromRoot, alpha, beta);
                        if (qScore < beta)
                        {
                            return int.Max(qScore, score);
                        }
                    }

                    canPrune = true;
                }
            }
        }

        if (depth <= 0)
        {
            // Max depth reached, return evaluation of quiet position
            NodesVisited--;
            return QuiescenceSearch(depthFromRoot, alpha, beta);
        }

        if (transpositionType == default && depth > SpsaOptions.InternalIterativeDeepeningDepth)
        {
            // Internal iterative deepening
            depth--;
        }

        // Best move seen so far used in move ordering.
        var moveOrderingBestMove = depthFromRoot == 0
            ? BestSoFar
            : transpositionBestMove;
        // Generate pseudo legal moves from this position
        var moves = stackalloc uint[218];
        var psuedoMoveCount = pboard.GeneratePseudoLegalMoves(moves, false);

        if (psuedoMoveCount == 0)
        {
            // No available moves, either stalemate or checkmate
            var eval = MoveScoring.EvaluateFinalPosition(depthFromRoot, parentInCheck);

            TranspositionTableExtensions.Set(Transpositions, TtMask, pHash, (byte)depth, depthFromRoot, eval,
                TranspositionTableFlag.Exact);

            return eval;
        }

        // Get counter move
        var counterMove = prevMove == default
            ? default
            : counters[prevMove.GetMovedPiece() * 64 + prevMove.GetToSquare()];

        // Get killer move
        var killerA = killers[depthFromRoot * 2];
        var killerB = killers[depthFromRoot * 2 + 1];

        // Data used in move ordering
        var scores = stackalloc int[psuedoMoveCount];
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

        for (var i = 0; i < psuedoMoveCount; i++)
        {
            // Estimate each moves score for move ordering
            scores[i] = pboard.ScoreMove(history, occupancyBitBoards, captures, moves[i], killerA, killerB,
                moveOrderingBestMove,
                counterMove);
        }

        var logDepth = Math.Log(depth);
        var searchedMoves = 0;

        uint bestMove = default;
        var evaluationBound = TranspositionTableFlag.Alpha;
        ref var parentBoardAccumulator = ref SearchStack[depthFromRoot].AccumulatorState;

        // Evaluate each move
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
                // Illegal move, undo
                continue;
            }

            board.UpdateCheckStatus();

            var isPromotionThreat = m.IsPromotionThreat();
            var isInteresting = parentInCheck || board.InCheck || isPromotionThreat ||
                                scores[moveIndex] > SpsaOptions.InterestingNegaMaxMoveScore;

            if (canPrune &&
                !isInteresting &&
                searchedMoves > depth * depth + SpsaOptions.LateMovePruningConstant)
            {
                // Late move pruning
                continue;
            }

            accumulator.UpdateToParent(ref parentBoardAccumulator, ref board);
            board.FinishApply(ref accumulator, m, pboard.EnPassantFile, pboard.CastleRights);
            MoveStack[board.TurnCount - 1] = board.Hash;

            Sse.Prefetch0(Transpositions + (board.Hash & TtMask));

            var needsFullSearch = true;
            var score = 0;

            if (searchedMoves > 0)
            {
                if (depth >= SpsaOptions.LateMoveReductionMinDepth && searchedMoves >= SpsaOptions.LateMoveReductionMinMoves)
                {
                    // LMR: Move ordering should ensure a better move has already been found by now so do a shallow search
                    var reduction = (int)(isInteresting
                        ? SpsaOptions.LateMoveReductionInterestingA + logDepth * Math.Log(searchedMoves) / SpsaOptions.LateMoveReductionInterestingB
                        : SpsaOptions.LateMoveReductionA + logDepth * Math.Log(searchedMoves) / SpsaOptions.LateMoveReductionB);

                    if (reduction > 0)
                    {
                        score = -NegaMaxSearch(depthFromRoot + 1, depth - reduction - 1,
                            -alpha - 1, -alpha, false, m);
                        needsFullSearch = score > alpha;
                    }
                }

                if (needsFullSearch)
                {
                    // PVS
                    score = -NegaMaxSearch(depthFromRoot + 1, depth - 1, -alpha - 1, -alpha,
                        false, m);
                    needsFullSearch = score > alpha && score < beta;
                }
            }

            if (needsFullSearch)
            {
                // Full search
                score = -NegaMaxSearch(depthFromRoot + 1, depth - 1, -beta, -alpha, false,
                    m);
            }

            // Revert the move

            searchedMoves++;

            if (_searchCancelled)
            {
                // Search was cancelled
                return 0;
            }

            if (score <= alpha)
            {
                // Move didn't cause an alpha cut off, continue searching
                continue;
            }

            // Update best move seen so far
            bestMove = m;
            alpha = score;
            evaluationBound = TranspositionTableFlag.Exact;

            if (score >= beta)
            {
                // Cache in transposition table
                TranspositionTableExtensions.Set(Transpositions, TtMask, pHash, (byte)depth, depthFromRoot,
                    score,
                    TranspositionTableFlag.Beta,
                    bestMove);

                if (m.IsQuiet())
                {
                    // Update move ordering heuristics for quiet moves that lead to a beta cut off

                    // History
                    HistoryHeuristicExtensions.UpdateMovesHistory(history, moves, moveIndex, m, depth);

                    if (m != killerA)
                    {
                        // Killer move
                        killers[depthFromRoot * 2 + 1] = killerA;
                        killers[depthFromRoot * 2] = m;
                    }

                    if (prevMove != default)
                    {
                        // Counter move
                        counters[prevMove.GetMovedPiece() * 64 + prevMove.GetToSquare()] = m;
                    }
                }

                return score;
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

        if (searchedMoves == 0)
        {
            // No available moves, either stalemate or checkmate
            var eval = MoveScoring.EvaluateFinalPosition(depthFromRoot, parentInCheck);

            TranspositionTableExtensions.Set(Transpositions, TtMask, pHash, (byte)depth, depthFromRoot, eval,
                TranspositionTableFlag.Exact);

            return eval;
        }

        // Cache in transposition table
        TranspositionTableExtensions.Set(Transpositions, TtMask, pHash, (byte)depth, depthFromRoot, alpha,
            evaluationBound, bestMove);

        return alpha;
    }
}