using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Search;

public partial class Searcher
{
    public unsafe int
        NegaMaxSearch(BoardStateData* currentBoardState, AccumulatorState* currentAccumulatorState, int depthFromRoot, int depth,
            int alpha, int beta)
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
            return Evaluate(currentBoardState, currentAccumulatorState, depthFromRoot);
        }

        var newAccumulatorState = currentAccumulatorState + 1; 
        var newBoardState = currentBoardState + 1;

        var pvIndex = *(PVTable.Indexes + depthFromRoot);
        var nextPvIndex = *(PVTable.Indexes + depthFromRoot + 1);
        _pVTable[pvIndex] = 0;

        var pvNode = beta - alpha > 1;
        var parentInCheck = currentBoardState->InCheck;
        var pHash = currentBoardState->Hash;

        var canPrune = false;
        uint transpositionBestMove = default;
        TranspositionTableFlag transpositionType = default;
        var transpositionEvaluation = TranspositionTableExtensions.NoHashEntry;
        ref var ttEntry = ref *(Transpositions + (pHash & TtMask));
        var corrhistIndex = CorrectionIndex(currentBoardState->PawnHash, currentBoardState->WhiteToMove);
        var staticEval = AdjustEval(corrhistIndex,
            Evaluate(currentBoardState, currentAccumulatorState, depthFromRoot));

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

            if (currentBoardState->HalfMoveClock >= 100 || currentBoardState->InsufficientMatingMaterial())
            {
                // Detect draw by Fifty move counter or repetition
                return 0;
            }

            if (alpha < 0 && currentBoardState->HasRepetition(HashHistory, depthFromRoot))
            {
                alpha = 0;
                if (alpha >= beta)
                    return alpha;
            }

            if (pHash == ttEntry.FullHash)
            {
                transpositionBestMove = ttEntry.Move;
                transpositionType = ttEntry.Flag;
                if (ttEntry.Depth >= depth)
                {
                    var score = TranspositionTableExtensions.RecalculateMateScores(ttEntry.Evaluation, depthFromRoot);

                    transpositionEvaluation = ttEntry.Flag switch
                    {
                        TranspositionTableFlag.Exact => score,
                        TranspositionTableFlag.Alpha when score <= alpha => alpha,
                        TranspositionTableFlag.Beta when score >= beta => beta,
                        _ => TranspositionTableExtensions.NoHashEntry
                    };

                    if (!pvNode && transpositionEvaluation != TranspositionTableExtensions.NoHashEntry)
                    {
                        // Transposition table hit
                        return transpositionEvaluation;
                    }
                }
            }

            if (parentInCheck)
            {
                // Extend searches when in check
                depth++;
            }
            else if (!pvNode)
            {
                // Reverse futility pruning
                var margin = depth * SpsaOptions.ReverseFutilityPruningMargin;
                if (depth <= SpsaOptions.ReverseFutilityPruningDepth && staticEval >= beta + margin)
                {
                    return staticEval - margin;
                }

                // Null move pruning
                if (staticEval >= beta &&
                    currentAccumulatorState->Move != default &&
                    (transpositionType != TranspositionTableFlag.Alpha || transpositionEvaluation >= beta) &&
                    depth > SpsaOptions.NullMovePruningDepth && currentBoardState->HasMajorPieces())
                {
                    var reduction = Math.Max(0, (depth - SpsaOptions.NullMovePruningReductionA) / SpsaOptions.NullMovePruningReductionB + SpsaOptions.NullMovePruningReductionC);

                    Unsafe.CopyBlock(newBoardState, currentBoardState, BoardStateData.BoardStateSize);

                    newBoardState->ApplyNullMove();

                    newAccumulatorState->UpdateTo(newBoardState);
                    newAccumulatorState->Move = default;

                    var nullMoveScore = -NegaMaxSearch(newBoardState, newAccumulatorState, depthFromRoot + 1,
                        Math.Max(depth - reduction - 1, 0), -beta,
                        -beta + 1);

                    if (nullMoveScore >= beta)
                    {
                        // Beta cutoff

                        // Cache in Transposition table
                        ttEntry.Set(pHash, (byte)depth,
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
                        var qScore = QuiescenceSearch(currentBoardState, currentAccumulatorState, depthFromRoot, alpha, beta);

                        return int.Max(qScore, score);
                    }

                    score = staticEval + SpsaOptions.RazorMarginB;
                    if (score < beta)
                    {
                        NodesVisited--;
                        var qScore = QuiescenceSearch(currentBoardState, currentAccumulatorState, depthFromRoot, alpha, beta);
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
            return QuiescenceSearch(currentBoardState, currentAccumulatorState, depthFromRoot, alpha, beta);
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
        var psuedoMoveCount = currentBoardState->GeneratePseudoLegalMoves(moves, false);

        if (psuedoMoveCount == 0)
        {
            // No available moves, either stalemate or checkmate
            var eval = MoveScoring.EvaluateFinalPosition(depthFromRoot, parentInCheck);

            if (!currentBoardState->InCheck)
            {
                var diff = eval - staticEval;
                UpdateCorrectionHistory(corrhistIndex, diff, depth);
            }

            ttEntry.Set(pHash, (byte)depth, depthFromRoot, eval,
                TranspositionTableFlag.Exact);

            return eval;
        }

        // Get counter move
        var counterMoveIndex = currentAccumulatorState->Move.GetCounterMoveIndex();
        var counterMove = counterMoveIndex == default ? default: *(Counters + counterMoveIndex);

        // Get killer move
        var killerA = *(killers + (depthFromRoot << 1));
        var killerB = *(killers + (depthFromRoot << 1) + 1);

        // Data used in move ordering
        var scores = stackalloc int[psuedoMoveCount];
        var occupancyBitBoards = stackalloc ulong[8]
        {
            currentBoardState->Occupancy[Constants.WhitePieces],
            currentBoardState->Occupancy[Constants.BlackPieces],
            currentBoardState->Occupancy[Constants.BlackPawn] | currentBoardState->Occupancy[Constants.WhitePawn],
            currentBoardState->Occupancy[Constants.BlackKnight] | currentBoardState->Occupancy[Constants.WhiteKnight],
            currentBoardState->Occupancy[Constants.BlackBishop] | currentBoardState->Occupancy[Constants.WhiteBishop],
            currentBoardState->Occupancy[Constants.BlackRook] | currentBoardState->Occupancy[Constants.WhiteRook],
            currentBoardState->Occupancy[Constants.BlackQueen] | currentBoardState->Occupancy[Constants.WhiteQueen],
            currentBoardState->Occupancy[Constants.BlackKing] | currentBoardState->Occupancy[Constants.WhiteKing]
        };

        var captures = stackalloc short[currentBoardState->PieceCount];

        for (var i = 0; i < psuedoMoveCount; i++)
        {
            // Estimate each moves score for move ordering
            *(scores + i) = currentBoardState->ScoreMove(History, occupancyBitBoards, captures, *(moves + i), killerA, killerB,
                moveOrderingBestMove,
                counterMove);
        }

        var logDepth = MathHelpers.LogLookup[depth];
        var searchedMoves = 0;

        uint bestMove = default;
        var evaluationBound = TranspositionTableFlag.Alpha;
        var nextHashHistoryEntry = HashHistory + currentBoardState->TurnCount;

        var bestScore = int.MinValue;
        // Evaluate each move
        for (var moveIndex = 0; moveIndex < psuedoMoveCount; ++moveIndex)
        {
            var currentScorePtr = scores + moveIndex;
            var currentMovePtr = moves + moveIndex;

            // Incremental move sorting
            for (var j = moveIndex + 1; j < psuedoMoveCount; j++)
            {
                // Pointer to the score and move being compared
                var compareScorePtr = scores + j;
                var compareMovePtr = moves + j;

                if (*compareScorePtr > *currentScorePtr)
                {
                    // Swap the scores and moves directly using pointers
                    // Using temporary variables for clarity
                    int tempScore = *currentScorePtr;
                    uint tempMove = *currentMovePtr;

                    *currentScorePtr = *compareScorePtr;
                    *currentMovePtr = *compareMovePtr;

                    *compareScorePtr = tempScore;
                    *compareMovePtr = tempMove;
                }
            }

            Unsafe.CopyBlock(newBoardState, currentBoardState, BoardStateData.BoardStateSize);

            var m = *currentMovePtr;

            if (!newBoardState->PartialApply(m))
            {
                *currentMovePtr = default;

                // Illegal move, undo
                continue;
            }

            newBoardState->UpdateCheckStatus();

            var isPromotionThreat = m.IsPromotionThreat();
            var isInteresting = parentInCheck || newBoardState->InCheck || isPromotionThreat ||
                                (*currentScorePtr) > SpsaOptions.InterestingNegaMaxMoveScore;

            if (canPrune &&
                !isInteresting &&
                searchedMoves > depth * depth + SpsaOptions.LateMovePruningConstant)
            {
                // Late move pruning
                continue;
            }

            newAccumulatorState->UpdateToParent(currentAccumulatorState, newBoardState);
            newBoardState->FinishApply(ref *newAccumulatorState, m, currentBoardState->EnPassantFile, currentBoardState->CastleRights);
            *nextHashHistoryEntry = newBoardState->Hash;

            Sse.Prefetch0(Transpositions + (newBoardState->Hash & TtMask));

            var needsFullSearch = true;
            var score = 0;

            if (searchedMoves > 0)
            {
                if (depth >= SpsaOptions.LateMoveReductionMinDepth && searchedMoves >= SpsaOptions.LateMoveReductionMinMoves)
                {
                    // LMR: Move ordering should ensure a better move has already been found by now so do a shallow search
                    var reduction = (int)(isInteresting
                        ? SpsaOptions.LateMoveReductionInterestingA + logDepth * MathHelpers.LogLookup[searchedMoves] / SpsaOptions.LateMoveReductionInterestingB
                        : SpsaOptions.LateMoveReductionA + logDepth * MathHelpers.LogLookup[searchedMoves] / SpsaOptions.LateMoveReductionB);

                    if (reduction > 0)
                    {
                        score = -NegaMaxSearch(newBoardState, newAccumulatorState, depthFromRoot + 1, depth - reduction - 1,
                            -alpha - 1, -alpha);
                        needsFullSearch = score > alpha;
                    }
                }

                if (needsFullSearch)
                {
                    // PVS
                    score = -NegaMaxSearch(newBoardState, newAccumulatorState, depthFromRoot + 1, depth - 1, -alpha - 1, -alpha);
                    needsFullSearch = score > alpha && score < beta;
                }
            }

            if (needsFullSearch)
            {
                // Full search
                score = -NegaMaxSearch(newBoardState, newAccumulatorState, depthFromRoot + 1, depth - 1, -beta, -alpha);
            }

            // Revert the move

            searchedMoves++;

            if (_searchCancelled)
            {
                // Search was cancelled
                return 0;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = m;
            }

            if (score <= alpha)
            {
                // Move didn't cause an alpha cut off, continue searching
                continue;
            }

            // Update best move seen so far
            alpha = score;
            evaluationBound = TranspositionTableFlag.Exact;

            if (score >= beta)
            {
                if (!currentBoardState->InCheck && (bestMove == default || !bestMove.IsCapture())
                                                && !(score >= staticEval))
                {
                    var diff = bestScore - staticEval;
                    UpdateCorrectionHistory(corrhistIndex, diff, depth);
                }

                // Cache in transposition table
                ttEntry.Set(pHash, (byte)depth, depthFromRoot,
                    score,
                    TranspositionTableFlag.Beta,
                    bestMove);

                if (m.IsQuiet())
                {
                    // Update move ordering heuristics for quiet moves that lead to a beta cut off

                    // History
                    HistoryHeuristicExtensions.UpdateMovesHistory(History, moves, moveIndex, m, depth);

                    if (m != killerA)
                    {
                        // Killer move
                        *(killers + (depthFromRoot << 1) + 1) = killerA;
                        *(killers + (depthFromRoot << 1)) = m;
                    }

                    if (counterMoveIndex != 0)
                    {
                        *(Counters + counterMoveIndex) = m;
                    }
                }

                return score;
            }

            if (!_searchCancelled)
            {
                // update pv table
                *(_pVTable + pvIndex) = m;
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

            if (!currentBoardState->InCheck)
            {
                var diff = eval - staticEval;
                UpdateCorrectionHistory(corrhistIndex, diff, depth);
            }

            ttEntry.Set(pHash, (byte)depth, depthFromRoot, eval,
                TranspositionTableFlag.Exact);

            return eval;
        }


        if (!currentBoardState->InCheck
                            && (bestMove == default || !bestMove.IsCapture())
                            && !(evaluationBound == TranspositionTableFlag.Alpha && alpha <= staticEval)
                            && !(evaluationBound == TranspositionTableFlag.Beta && alpha >= staticEval))
        {
            var diff = bestScore - staticEval;
            UpdateCorrectionHistory(corrhistIndex, diff, depth);
        }

        // Cache in transposition table
        ttEntry.Set(pHash, (byte)depth, depthFromRoot, alpha,
            evaluationBound, bestMove);

        return alpha;
    }
}