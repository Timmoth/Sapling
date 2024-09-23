using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Search;

public partial class Searcher
{
    public unsafe int
        NegaMaxSearch(ref BoardStateData board, VectorShort* whiteAcc, VectorShort* blackAcc, ulong* hashHistory, uint* killers, uint* counters, int* history, int depthFromRoot, int depth,
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
            return board.Evaluate(whiteAcc, blackAcc);
        }

        var pvIndex = PVTable.Indexes[depthFromRoot];
        var nextPvIndex = PVTable.Indexes[depthFromRoot + 1];
        _pVTable[pvIndex] = 0;

        var pvNode = beta - alpha > 1;
        var inCheck = board.InCheck;
        var originalHash = board.Hash;
        var oldEnpassant = board.EnPassantFile;
        var prevHalfMoveClock = board.HalfMoveClock;

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

            if (board.HalfMoveClock >= 100 || board.InsufficientMatingMaterial())
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

            (var transpositionEvaluation, transpositionBestMove, transpositionType) =
                TranspositionTableExtensions.Get(_transpositionTable, TtMask, board.Hash, depth, depthFromRoot, alpha,
                    beta);

            if (!pvNode && transpositionEvaluation != TranspositionTableExtensions.NoHashEntry)
            {
                // Transposition table hit
                return transpositionEvaluation;
            }

            if (inCheck)
            {
                // Extend searches when in check
                depth++;
            }
            else if (!pvNode)
            {
                var staticEval = board.Evaluate(whiteAcc, blackAcc);

                // Reverse futility pruning
                var margin = depth * 75;
                if (depth <= 7 && staticEval >= beta + margin)
                {
                    return staticEval - margin;
                }

                // Null move pruning
                if (staticEval >= beta &&
                    !wasReducedMove &&
                    (transpositionType != TranspositionTableFlag.Alpha || transpositionEvaluation >= beta) &&
                    depth > 2 && board.HasMajorPieces(board.WhiteToMove))
                {
                    var reduction = Math.Max(0, (depth - 3) / 4 + 3);

                    board.ApplyNullMove();
                    var nullMoveScore = -NegaMaxSearch(ref board, whiteAcc, blackAcc, hashHistory, killers,  counters, history, depthFromRoot + 1,
                        Math.Max(depth - reduction - 1, 0), -beta,
                        -beta + 1, true, prevMove);
                    board.UnApplyNullMove(originalHash, oldEnpassant, inCheck, prevHalfMoveClock);

                    if (nullMoveScore >= beta)
                    {
                        // Beta cutoff

                        // Cache in Transposition table
                        TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, (byte)depth,
                            depthFromRoot,
                            beta, TranspositionTableFlag.Beta);
                        return beta;
                    }
                }

                // Razoring
                if (depth is > 0 and <= 3)
                {
                    var score = staticEval + 125;
                    if (depth == 1 && score < beta)
                    {
                        NodesVisited--;
                        var qScore = QuiescenceSearch(ref board, whiteAcc, blackAcc, hashHistory, depthFromRoot, alpha, beta);

                        return int.Max(qScore, score);
                    }

                    score += 175;
                    if (score < beta)
                    {
                        NodesVisited--;
                        var qScore = QuiescenceSearch(ref board, whiteAcc, blackAcc, hashHistory, depthFromRoot, alpha, beta);
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
            return QuiescenceSearch(ref board, whiteAcc, blackAcc, hashHistory, depthFromRoot, alpha, beta);
        }

        if (transpositionType == default && depth > 2)
        {
            // Internal iterative deepening
            depth--;
        }

        var prevCastleRights = board.CastleRights;
        // Best move seen so far used in move ordering.
        var moveOrderingBestMove = depthFromRoot == 0
            ? BestSoFar
            : transpositionBestMove;
        // Generate pseudo legal moves from this position
        var moves = stackalloc uint[218];
        var psuedoMoveCount = board.GeneratePseudoLegalMoves(moves, false);

        if (psuedoMoveCount == 0)
        {
            // No available moves, either stalemate or checkmate
            var eval = MoveScoring.EvaluateFinalPosition(depthFromRoot, inCheck);

            TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, (byte)depth, depthFromRoot, eval,
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
            // Estimate each moves score for move ordering
            scores[i] = board.ScoreMove(history, occupancyBitBoards, captures, moves[i], killerA, killerB,
                moveOrderingBestMove,
                counterMove);
        }

        var logDepth = Math.Log(depth);
        var searchedMoves = 0;

        uint bestMove = default;
        var evaluationBound = TranspositionTableFlag.Alpha;

        var whiteAccPtr = stackalloc VectorShort[NnueEvaluator.AccumulatorSize];
        var blackAccPtr = stackalloc VectorShort[NnueEvaluator.AccumulatorSize];
        BoardStateData copy = default;

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

            board.CloneTo(ref copy);

            var m = moves[moveIndex];

            if (!copy.PartialApply(m))
            {
                // Illegal move, undo
                continue;
            }

            copy.UpdateCheckStatus();

            var isPromotionThreat = m.IsPromotionThreat();
            var isInteresting = inCheck || copy.InCheck || isPromotionThreat ||
                                scores[moveIndex] > Constants.LosingCaptureBias;

            if (canPrune &&
                !isInteresting &&
                searchedMoves > depth * depth + 8)
            {
                // Late move pruning
                continue;
            }

            NnueEvaluator.SimdCopy(whiteAccPtr, whiteAcc);
            NnueEvaluator.SimdCopy(blackAccPtr, blackAcc);
            // Finish making the move 
            copy.FinishApply(whiteAccPtr, blackAccPtr, hashHistory, m, oldEnpassant, prevCastleRights);

            Sse.Prefetch0(_transpositionTable + (copy.Hash & TtMask));

            var needsFullSearch = true;
            var score = 0;

            if (searchedMoves > 0)
            {
                if (depth >= 3 && searchedMoves >= 2)
                {
                    // LMR: Move ordering should ensure a better move has already been found by now so do a shallow search
                    var reduction = (int)(isInteresting
                        ? 0.2 + logDepth * Math.Log(searchedMoves) / 3.3
                        : 1.35 + logDepth * Math.Log(searchedMoves) / 2.75);


                    if (reduction > 0)
                    {
                        score = -NegaMaxSearch(ref copy, whiteAccPtr, blackAccPtr, hashHistory, killers, counters, history, depthFromRoot + 1, depth - reduction - 1,
                            -alpha - 1, -alpha, false, m);
                        needsFullSearch = score > alpha;
                    }
                }

                if (needsFullSearch)
                {
                    // PVS
                    score = -NegaMaxSearch(ref copy, whiteAccPtr, blackAccPtr, hashHistory, killers, counters, history, depthFromRoot + 1, depth - 1, -alpha - 1, -alpha,
                        false, m);
                    needsFullSearch = score > alpha && score < beta;
                }
            }

            if (needsFullSearch)
            {
                // Full search
                score = -NegaMaxSearch(ref copy, whiteAccPtr, blackAccPtr, hashHistory, killers, counters, history, depthFromRoot + 1, depth - 1, -beta, -alpha, false,
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
                TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, (byte)depth, depthFromRoot,
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
            var eval = MoveScoring.EvaluateFinalPosition(depthFromRoot, inCheck);

            TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, (byte)depth, depthFromRoot, eval,
                TranspositionTableFlag.Exact);

            return eval;
        }

        // Cache in transposition table
        TranspositionTableExtensions.Set(_transpositionTable, TtMask, board.Hash, (byte)depth, depthFromRoot, alpha,
            evaluationBound, bestMove);

        return alpha;
    }
}