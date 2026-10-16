using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;
using Sapling.Engine.Evaluation;

namespace Sapling.Engine.Search;

public partial class Searcher
{
    // Scores at or above this bound are mate scores; pruning that returns static
    // evaluations or beta must not operate on them.
    private const int MateScoreLimit = Constants.ImmediateMateScore - Constants.MaxSearchDepth;

    public unsafe int
        NegaMaxSearch(BoardStateData* currentBoardState, AccumulatorState* currentAccumulatorState, int depthFromRoot, int depth,
            int alpha, int beta)
    {
        NodesVisited++;
        var rootStartNodes = NodesVisited;
        long[]? rootMoveNodeCounts = null;
        if (depthFromRoot == 0)
        {
            rootMoveNodeCounts = RootMoveNodeCounts;
            Array.Clear(rootMoveNodeCounts);
        }

        if (depthFromRoot >= Constants.MaxSearchDepth || currentBoardState->TurnCount == ushort.MaxValue)
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
        var ttSnapshot = TranspositionTableExtensions.Probe(ref ttEntry, SharedTranspositions);

        if (depthFromRoot > 0)
        {
            if (ShouldStop())
            {
                // Search was cancelled, return
                return 0;
            }

            var mateScore = Constants.ImmediateMateScore - depthFromRoot;
            alpha = Math.Max(alpha, -mateScore);
            beta = Math.Min(beta, mateScore);
            if (alpha >= beta)
            {
                // Faster mating sequence found
                return alpha;
            }

            var isDraw = currentBoardState->HalfMoveClock >= 100 ||
                currentBoardState->InsufficientMatingMaterial() ||
                RepetitionDetector.IsThreefoldRepetition(currentBoardState->TurnCount, currentBoardState->HalfMoveClock, HashHistory);
            if (isDraw)
            {
                if (currentBoardState->InCheck && !currentBoardState->HasAnyLegalMove())
                    return MoveScoring.EvaluateFinalPosition(depthFromRoot, true);
                // Detect draw by Fifty move counter or repetition
                return 0;
            }

            if (alpha < 0 && currentBoardState->HasRepetition(HashHistory, depthFromRoot))
            {
                alpha = 0;
                if (alpha >= beta)
                    return alpha;
            }

            if (ttSnapshot.Valid && pHash == ttSnapshot.FullHash)
            {
                transpositionBestMove = ttSnapshot.Move;
                transpositionType = ttSnapshot.Flag;
                if (ttSnapshot.Depth >= depth)
                {
                    var score = TranspositionTableExtensions.RecalculateMateScores(ttSnapshot.Evaluation, depthFromRoot);

                    transpositionEvaluation = ttSnapshot.Flag switch
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
        }

        var pawnChIndex = CorrectionIndex(currentBoardState->PawnHash, currentBoardState->WhiteToMove);
        var whiteMaterialChIndex = CorrectionIndex(currentBoardState->WhiteMaterialHash, currentBoardState->WhiteToMove);
        var blackMaterialChIndex = CorrectionIndex(currentBoardState->BlackMaterialHash, currentBoardState->WhiteToMove);
        var previousMoveIndex = currentAccumulatorState->Move == default
            ? 0
            : currentAccumulatorState->Move.GetCounterMoveIndex();

        var rawStaticEval = Evaluate(currentBoardState, currentAccumulatorState, depthFromRoot);
        var staticEval = AdjustEval(pawnChIndex, whiteMaterialChIndex, blackMaterialChIndex, previousMoveIndex,
            rawStaticEval);
        var previousSameSideEval = depthFromRoot >= 2 && currentAccumulatorState->Move != default &&
            currentAccumulatorState[-1].Move != default ? currentAccumulatorState[-2].Evaluation :
            TranspositionTableExtensions.NoHashEntry;
        var improving = previousSameSideEval != TranspositionTableExtensions.NoHashEntry &&
            rawStaticEval > previousSameSideEval;

        if (depthFromRoot > 0)
        {
            if (parentInCheck)
            {
                // Extend searches when in check
                depth++;
            }
        }

        if (depth <= 0)
        {
            // Reduced LMR searches can reach or pass the horizon. Enter quiescence
            // before selective pruning so negative-depth nodes cannot use depth-scaled
            // margins and depth-zero stalemates retain their terminal score.
            NodesVisited--;
            return QuiescenceSearch(currentBoardState, currentAccumulatorState, depthFromRoot, alpha, beta);
        }

        var hasDeepExactTtEntry = ttSnapshot.Valid && pHash == ttSnapshot.FullHash &&
                                  ttSnapshot.Depth >= depth && ttSnapshot.Flag == TranspositionTableFlag.Exact;

        if (depthFromRoot > 0 && !parentInCheck && !pvNode &&
            !IsMateBand(staticEval) && !IsMateBand(alpha) && !IsMateBand(beta))
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
                depth > SpsaOptions.NullMovePruningDepth && currentBoardState->HasMajorPieces() &&
                ((currentBoardState->Occupancy[Constants.WhiteRook] | currentBoardState->Occupancy[Constants.BlackRook] |
                  currentBoardState->Occupancy[Constants.WhiteQueen] | currentBoardState->Occupancy[Constants.BlackQueen]) != 0))
            {
                var reduction = *(SpsaOptions.NullMovePruningReductionTable + depth);

                Unsafe.CopyBlock(newBoardState, currentBoardState, BoardStateData.BoardStateSize);

                newBoardState->ApplyNullMove();

                newAccumulatorState->UpdateTo(newBoardState);
                newAccumulatorState->Move = default;

                var nullMoveScore = -NegaMaxSearch(newBoardState, newAccumulatorState, depthFromRoot + 1,
                    Math.Max(depth - reduction - 1, 0), -beta,
                    -beta + 1);

                if (Interrupted())
                    return 0;

                if (nullMoveScore >= beta)
                {
                    // Beta cutoff
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
                    if (Interrupted())
                        return 0;

                    return int.Max(qScore, score);
                }

                score = staticEval + SpsaOptions.RazorMarginB;
                if (score < beta)
                {
                    NodesVisited--;
                    var qScore = QuiescenceSearch(currentBoardState, currentAccumulatorState, depthFromRoot, alpha, beta);
                    if (Interrupted())
                        return 0;
                    if (qScore < beta)
                    {
                        return int.Max(qScore, score);
                    }
                }

                canPrune = true;
            }
        }

        if (transpositionType == default && depth > SpsaOptions.InternalIterativeDeepeningDepth)
        {
            // Internal iterative deepening
            depth--;
        }

        var whiteToMove = currentBoardState->WhiteToMove;
        var oldEnpassantHashFile = Zobrist.GetEnPassantHashFile(ref *currentBoardState);

        var moves = stackalloc uint[MaxMovesPerPly];

        // Generate pseudo legal moves from this position
        var psuedoMoveCount = currentBoardState->GeneratePseudoLegalMoves(moves, false);

        if (psuedoMoveCount == 0)
        {
            // No available moves, either stalemate or checkmate
            var eval = MoveScoring.EvaluateFinalPosition(depthFromRoot, parentInCheck);

            if (!currentBoardState->InCheck)
            {
                var diff = eval - staticEval;
                UpdateCorrectionHistory(pawnChIndex, whiteMaterialChIndex, blackMaterialChIndex, previousMoveIndex, diff, depth);
            }

            StoreTransposition(ref ttEntry, pHash, (byte)depth, depthFromRoot, eval,
                TranspositionTableFlag.Exact);

            return eval;
        }

        // Best move seen so far used in move ordering.
        var moveOrderingBestMove = depthFromRoot == 0
            ? BestSoFar
            : transpositionBestMove;

        // Get counter move
        var counterMoveIndex = currentAccumulatorState->Move.GetCounterMoveIndex();
        var counterMove = counterMoveIndex == default ? default : *(Counters + counterMoveIndex);

        // Get killer move
        var killerA = *(killers + (depthFromRoot << 1));

        // Data used in move ordering
        var scores = stackalloc int[MaxMovesPerPly];

        var captures = stackalloc short[64];

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

        for (var i = 0; i < psuedoMoveCount; i++)
        {
            var move = *(moves + i);
            if (moveOrderingBestMove == move)
            {
                *(scores + i) = SpsaOptions.MoveOrderingBestMoveBias;
                continue;
            }

            if (move.IsCapture())
            {
                if (move.IsEnPassant())
                {
                    *(scores + i) = SpsaOptions.MoveOrderingEnPassantMoveBias;
                    continue;
                }

                var captureDelta = currentBoardState->StaticExchangeEvaluation(occupancyBitBoards, captures, move);

                *(scores + i) = (captureDelta >= 0 ? SpsaOptions.MoveOrderingWinningCaptureBias : SpsaOptions.MoveOrderingLosingCaptureBias) +
                                captureDelta + (move.IsPromotion() ? SpsaOptions.MoveOrderingCapturePromoteBias : 0);
                continue;
            }

            if (move.IsPromotion())
            {
                *(scores + i) = SpsaOptions.MoveOrderingPromoteBias;
                continue;
            }

            if (killerA == move)
            {
                *(scores + i) = SpsaOptions.MoveOrderingKillerABias;
                continue;
            }

            if (counterMove == move)
            {
                *(scores + i) = SpsaOptions.MoveOrderingCounterMoveBias;
                continue;
            }

            *(scores + i) = *(History + move.GetCounterMoveIndex());
        }

        var nextHashHistoryEntry = HashHistory + currentBoardState->TurnCount;
        var probCutSortedUpTo = 0;

        // Probcut
        int probCutMargin = improving ? 97 : 252;
        int probBeta = beta + probCutMargin;
        if (!pvNode && !parentInCheck
            && currentAccumulatorState->Move != default
            && depth >= SpsaOptions.ProbCutMinDepth
            && Math.Abs(beta) < TranspositionTableExtensions.PositiveCheckmateDetectionLimit
            && ((!ttSnapshot.Valid || pHash != ttSnapshot.FullHash) || ttSnapshot.Depth < depth - SpsaOptions.ProbCutMinDepth || (ttSnapshot.Evaluation != TranspositionTableExtensions.NoHashEntry && ttSnapshot.Evaluation >= probBeta)))
        {

            for (; probCutSortedUpTo < psuedoMoveCount; ++probCutSortedUpTo)
            {
                var currentScorePtr = scores + probCutSortedUpTo;
                var currentMovePtr = moves + probCutSortedUpTo;

                // Incremental move sorting
                for (var j = probCutSortedUpTo + 1; j < psuedoMoveCount; j++)
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

                if (*currentScorePtr < SpsaOptions.MoveOrderingPromoteBias)
                {
                    break;
                }

                var m = *currentMovePtr;
                if (m.IsQuiet())
                {
                    continue;
                }

                Unsafe.CopyBlock(newBoardState, currentBoardState, BoardStateData.BoardStateSize);

                if (whiteToMove ? !newBoardState->PartialApplyWhite(m) : !newBoardState->PartialApplyBlack(m))
                {
                    *currentMovePtr = default;
                    // Illegal move, undo
                    continue;
                }

                newBoardState->UpdateCheckStatus();
                newAccumulatorState->UpdateToParent(currentAccumulatorState, newBoardState);
                if (whiteToMove)
                {
                    newBoardState->FinishApplyWhite(ref *newAccumulatorState, m, currentBoardState->EnPassantFile, oldEnpassantHashFile, currentBoardState->CastleRights);
                }
                else
                {
                    newBoardState->FinishApplyBlack(ref *newAccumulatorState, m, currentBoardState->EnPassantFile, oldEnpassantHashFile, currentBoardState->CastleRights);
                }
                *nextHashHistoryEntry = newBoardState->Hash;

                PrefetchTransposition(Transpositions + (newBoardState->Hash & TtMask));

                NodesVisited--;
                var score = -QuiescenceSearch(newBoardState, newAccumulatorState, depthFromRoot + 1, -probBeta, -probBeta + 1);

                if (Interrupted()) return 0;
                if (score >= probBeta)
                {
                    score = -NegaMaxSearch(newBoardState, newAccumulatorState, depthFromRoot + 1, depth - SpsaOptions.ProbCutMinDepth,
                        -probBeta, -probBeta + 1);

                }

                if (Interrupted()) return 0;
                if (score >= probBeta)
                {
                    StoreTransposition(ref ttEntry, pHash, (byte)(depth - SpsaOptions.ProbCutMinDepth), depthFromRoot,
                        score,
                        TranspositionTableFlag.Beta,
                        default);

                    return score;
                }
            }
        }

        var lmpDepth = depth * depth + SpsaOptions.LateMovePruningConstant;
        var searchedMoves = 0;

        uint bestMove = default;
        var evaluationBound = TranspositionTableFlag.Alpha;

        var bestScore = int.MinValue;
        // Quiet moves actually searched (failed-low or alpha-raising) before a beta cutoff. Used
        // for history malus so LMP-skipped / illegal / never-searched quiets are not penalized.
        // Keep the searched-quiet history set in a per-node stack buffer.
        var searchedQuiets = stackalloc uint[MaxMovesPerPly];
        var searchedQuietCount = 0;
        // Evaluate each move
        for (var moveIndex = 0; moveIndex < psuedoMoveCount; ++moveIndex)
        {
            var currentScorePtr = scores + moveIndex;
            var currentMovePtr = moves + moveIndex;

            if (moveIndex >= probCutSortedUpTo)
            {
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
            }

            var m = *currentMovePtr;

            if (m == default)
            {
                continue;
            }

            Unsafe.CopyBlock(newBoardState, currentBoardState, BoardStateData.BoardStateSize);

            if (whiteToMove ? !newBoardState->PartialApplyWhite(m) : !newBoardState->PartialApplyBlack(m))
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
                searchedMoves > lmpDepth)
            {
                // Late move pruning
                continue;
            }

            newAccumulatorState->UpdateToParent(currentAccumulatorState, newBoardState);
            if (whiteToMove)
            {
                newBoardState->FinishApplyWhite(ref *newAccumulatorState, m, currentBoardState->EnPassantFile, oldEnpassantHashFile, currentBoardState->CastleRights);
            }
            else
            {
                newBoardState->FinishApplyBlack(ref *newAccumulatorState, m, currentBoardState->EnPassantFile, oldEnpassantHashFile, currentBoardState->CastleRights);
            }
            *nextHashHistoryEntry = newBoardState->Hash;

            PrefetchTransposition(Transpositions + (newBoardState->Hash & TtMask));

            var needsFullSearch = true;
            var score = 0;
            var nodesBeforeMove = NodesVisited;

            if (searchedMoves > 0)
            {
                if (depth >= SpsaOptions.LateMoveReductionMinDepth && searchedMoves >= SpsaOptions.LateMoveReductionMinMoves)
                {
                    // LMR: Move ordering should ensure a better move has already been found by now so do a shallow search
                    var reduction = isInteresting
                        ? *(SpsaOptions.LateMovePruningInterestingReductionTable + depth * 218 + searchedMoves)
                        : *(SpsaOptions.LateMovePruningReductionTable + depth * 218 + searchedMoves);
                    if (hasDeepExactTtEntry)
                        reduction = LateMoveReduction.AdjustForExactTt(reduction, hasDeepExactTtEntry,
                            SpsaOptions.LateMoveReductionTtExactScale, depth - 1);
                    reduction = Math.Min(Math.Max(reduction, 0), depth - 1);

                    if (reduction > 0)
                    {
                        score = -NegaMaxSearch(newBoardState, newAccumulatorState, depthFromRoot + 1, depth - reduction - 1,
                            -alpha - 1, -alpha);
                        if (Interrupted())
                            return 0;
                        needsFullSearch = score > alpha;
                    }
                }

                if (needsFullSearch)
                {
                    // PVS
                    score = -NegaMaxSearch(newBoardState, newAccumulatorState, depthFromRoot + 1, depth - 1, -alpha - 1, -alpha);
                    if (Interrupted())
                        return 0;
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

            if (Interrupted())
            {
                // Search was cancelled or the slice was exhausted. The root must also stop
                // consuming moves: post-stop child calls return 0 immediately, which would
                // otherwise corrupt bestMove, correction history, killers and the root TT entry.
                break;
            }

            if (depthFromRoot == 0)
                rootMoveNodeCounts![moveIndex] = NodesVisited - nodesBeforeMove;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = m;
            }

            // Record searched quiets that did not produce the beta cutoff (failed-low or
            // alpha-raising). The cutoff winner is excluded and gets the bonus instead.
            if (m.IsQuiet() && score < beta)
                searchedQuiets[searchedQuietCount++] = m;

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
                if (!parentInCheck && (bestMove == default || bestMove.IsQuiet())
                                   && (score > staticEval))
                {
                    var diff = bestScore - staticEval;
                    UpdateCorrectionHistory(pawnChIndex, whiteMaterialChIndex, blackMaterialChIndex, previousMoveIndex, diff, depth);
                }

                // Cache in transposition table
                StoreTransposition(ref ttEntry, pHash, (byte)depth, depthFromRoot,
                    score,
                    TranspositionTableFlag.Beta,
                    bestMove);

                if (depthFromRoot == 0)
                    UpdateRootBestMoveNodeFraction(moves, rootMoveNodeCounts!, psuedoMoveCount, rootStartNodes, bestMove);

                if (m.IsQuiet())
                {
                    // Update move ordering heuristics for quiet moves that lead to a beta cut off

                    // History
                    HistoryHeuristicExtensions.UpdateMovesHistory(History, searchedQuiets, searchedQuietCount, m, depth);

                    *(killers + (depthFromRoot << 1)) = m;
                    if (counterMoveIndex != 0)
                    {
                        *(Counters + counterMoveIndex) = m;
                    }
                }

                return score;
            }

            if (!Interrupted() || depthFromRoot > 0)
            {
                // update pv table
                *(_pVTable + pvIndex) = m;
                ShiftPvMoves(pvIndex + 1, nextPvIndex, Constants.MaxSearchDepth - depthFromRoot - 1);
            }
        }

        if (Interrupted())
        {
            // Search was cancelled or the slice was exhausted: return without publishing TT
            // entries or correction history updates from partial work, at the root as well as
            // below it. Completed subtrees searched before the interruption remain cached.
            return 0;
        }

        if (searchedMoves == 0)
        {
            // No available moves, either stalemate or checkmate
            var eval = MoveScoring.EvaluateFinalPosition(depthFromRoot, parentInCheck);

            if (!parentInCheck)
            {
                var diff = eval - staticEval;
                UpdateCorrectionHistory(pawnChIndex, whiteMaterialChIndex, blackMaterialChIndex, previousMoveIndex, diff, depth);
            }

            StoreTransposition(ref ttEntry, pHash, (byte)depth, depthFromRoot, eval,
                TranspositionTableFlag.Exact);

            return eval;
        }

        if (!parentInCheck
                            && (bestMove == default || bestMove.IsQuiet())
                            && (evaluationBound == TranspositionTableFlag.Exact || bestScore < staticEval))
        {
            var diff = bestScore - staticEval;
            UpdateCorrectionHistory(pawnChIndex, whiteMaterialChIndex, blackMaterialChIndex, previousMoveIndex, diff, depth);
        }

        if (depthFromRoot == 0)
            UpdateRootBestMoveNodeFraction(moves, rootMoveNodeCounts!, psuedoMoveCount, rootStartNodes, bestMove);

        // Cache in transposition table
        StoreTransposition(ref ttEntry, pHash, (byte)depth, depthFromRoot, alpha,
            evaluationBound, bestMove);

        return alpha;
    }

    private unsafe void UpdateRootBestMoveNodeFraction(uint* moves, long[] nodeCounts, int count,
        long rootStartNodes, uint bestMove)
    {
        long total = 0;
        long best = 0;
        for (var i = 0; i < count; i++)
        {
            var nodes = nodeCounts[i];
            total += nodes;
            if (moves[i] == bestMove)
                best = nodes;
        }

        total = Math.Max(total, NodesVisited - rootStartNodes);
        RootBestMoveNodeFraction = total > 0 ? (double)best / total : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsMateBand(int score) => score >= MateScoreLimit || score <= -MateScoreLimit;
}
