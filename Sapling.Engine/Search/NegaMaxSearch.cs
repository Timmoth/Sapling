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
    public unsafe int
        NegaMaxSearch(Span<uint> killers, Span<uint> counters, Span<int> history, int depthFromRoot, int depth,
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
            return Board.Evaluate();
        }

        var pvIndex = PVTable.Indexes[depthFromRoot];
        var nextPvIndex = PVTable.Indexes[depthFromRoot + 1];
        _pVTable[pvIndex] = 0;

        var pvNode = beta - alpha > 1;
        var inCheck = Board.InCheck;
        var originalHash = Board.Hash;
        var oldEnpassant = Board.EnPassantFile;

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

            if (Board.HalfMoveClock >= 100 || Board.InsufficientMatingMaterial() ||
                RepetitionTable.DetectThreeFoldRepetition(Board.Hash))
            {
                // Detect draw by Fifty move counter or repetition
                return 0;
            }

            (var transpositionEvaluation, transpositionBestMove, transpositionType) =
                TranspositionTableExtensions.Get(_transpositionTable, TtMask, Board.Hash, depth, depthFromRoot, alpha,
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
                var staticEval = Board.Evaluate();

                // Reverse futility pruning
                if (depth <= 7 && staticEval >= beta + depth * 75)
                {
                    return staticEval;
                }

                // Null move pruning
                if (staticEval >= beta &&
                    !wasReducedMove &&
                    (transpositionType != TranspositionTableFlag.Alpha || transpositionEvaluation >= beta) &&
                    depth > 2 && Board.HasMajorPieces(Board.WhiteToMove))
                {
                    var reduction = Math.Max(0, (depth - 3) / 4 + 3);

                    Board.ApplyNullMove();
                    var nullMoveScore = -NegaMaxSearch(killers, counters, history, depthFromRoot + 1,
                        Math.Max(depth - reduction - 1, 0), -beta,
                        -beta + 1, true, prevMove);
                    Board.UnApplyNullMove(originalHash, oldEnpassant);

                    if (nullMoveScore >= beta)
                    {
                        // Beta cutoff

                        // Cache in Transposition table
                        TranspositionTableExtensions.Set(_transpositionTable, TtMask, Board.Hash, (byte)depth,
                            depthFromRoot,
                            beta, TranspositionTableFlag.Beta);
                        return beta;
                    }
                }

                // Razoring
                if (depth is > 0 and <= 3)
                {
                    var score = staticEval + 100;
                    if (depth == 1 && score < beta)
                    {
                        NodesVisited--;
                        var qScore = QuiescenceSearch(depthFromRoot, alpha, beta);

                        return qScore > score
                            ? qScore
                            : score;
                    }

                    score = staticEval + 250;
                    if (score < beta)
                    {
                        NodesVisited--;
                        var qScore = QuiescenceSearch(depthFromRoot, alpha, beta);
                        if (qScore < beta)
                        {
                            return qScore > score
                                ? qScore
                                : score;
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

        if (transpositionType == default && depth > 2)
        {
            // Internal iterative deepening
            depth--;
        }

        if (depthFromRoot > 0)
        {
            // Push board state onto repetition table
            RepetitionTable.Push(Board.Hash, prevMove.IsReset());
        }

        var prevCastleRights = Board.CastleRights;
        var prevFiftyMoveCounter = Board.HalfMoveClock;
        // Best move seen so far used in move ordering.
        var moveOrderingBestMove = depthFromRoot == 0
            ? BestSoFar
            : transpositionBestMove;
        // Generate pseudo legal moves from this position
        Span<uint> moves = stackalloc uint[218];
        var psuedoMoveCount = Board.GeneratePseudoLegalMoves(moves, false);

        // Get counter move
        var counterMove = prevMove == default
            ? default
            : counters[prevMove.GetMovedPiece() * 64 + prevMove.GetToSquare()];

        // Get killer move
        var killerA = killers[depthFromRoot * 2];
        var killerB = killers[depthFromRoot * 2 + 1];

        // Data used in move ordering
        Span<int> scores = stackalloc int[psuedoMoveCount];
        Span<ulong> occupancyBitBoards = stackalloc ulong[8]
        {
            Board.WhitePieces,
            Board.BlackPieces,
            Board.BlackPawns | Board.WhitePawns,
            Board.BlackKnights | Board.WhiteKnights,
            Board.BlackBishops | Board.WhiteBishops,
            Board.BlackRooks | Board.WhiteRooks,
            Board.BlackQueens | Board.WhiteQueens,
            Board.BlackKings | Board.WhiteKings
        };

        Span<short> captures = stackalloc short[Board.PieceCount];

        for (var i = 0; i < psuedoMoveCount; ++i)
        {
            // Estimate each moves score for move ordering
            scores[i] = Board.ScoreMove(history, occupancyBitBoards, captures, moves[i], killerA, killerB,
                moveOrderingBestMove,
                counterMove);
        }

        var logDepth = Math.Log(depth);
        var searchedMoves = 0;

        uint bestMove = default;
        var evaluationBound = TranspositionTableFlag.Alpha;

        var shouldWhiteMirrored = Board.Evaluator.ShouldWhiteMirrored;
        var shouldBlackMirrored = Board.Evaluator.ShouldBlackMirrored;
        var whiteMirrored = Board.Evaluator.WhiteMirrored;
        var blackMirrored = Board.Evaluator.BlackMirrored;
        var whiteAccPtr = stackalloc VectorShort[NnueEvaluator.AccumulatorSize];
        var blackAccPtr = stackalloc VectorShort[NnueEvaluator.AccumulatorSize];
 
        NnueEvaluator.SimdCopy(whiteAccPtr, Board.Evaluator.WhiteAccumulator);
        NnueEvaluator.SimdCopy(blackAccPtr, Board.Evaluator.BlackAccumulator);

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

            var m = moves[moveIndex];

            if (!Board.PartialApply(m))
            {
                // Illegal move, undo
                Board.PartialUnApply(m, originalHash, oldEnpassant, inCheck, prevCastleRights, prevFiftyMoveCounter);
                continue;
            }

            Board.UpdateCheckStatus();

            var isPromotionThreat = m.IsPromotionThreat();
            var isInteresting = inCheck || Board.InCheck || isPromotionThreat ||
                                scores[moveIndex] > Constants.LosingCaptureBias;
            if (canPrune &&
                !isInteresting &&
                searchedMoves > depth * depth + 5 * depth)
            {
                // Late move pruning
                Board.PartialUnApply(m, originalHash, oldEnpassant, inCheck, prevCastleRights, prevFiftyMoveCounter);
                continue;
            }

            // Finish making the move 
            Board.FinishApply(m, oldEnpassant, prevCastleRights);

            Sse.Prefetch0(_transpositionTable + (Board.Hash & TtMask));

            var needsFullSearch = true;
            var score = 0;

                if (searchedMoves > 0)
                {
                    if (!isInteresting && depth > 3 && searchedMoves >= 3)
                    {
                        // LMR: Move ordering should ensure a better move has already been found by now so do a shallow search
                        var reduction = (int)(pvNode
                            ? logDepth * Math.Log(searchedMoves) / 2
                            : 0.5 + logDepth * Math.Log(searchedMoves) / 2);


                        if (reduction > 0)
                        {
                            score = -NegaMaxSearch(killers, counters, history, depthFromRoot + 1, depth - reduction - 1,
                                -alpha - 1, -alpha, false, m);
                            needsFullSearch = score > alpha;
                        }
                    }

                    if (needsFullSearch)
                    {
                        // PVS
                        score = -NegaMaxSearch(killers, counters, history, depthFromRoot + 1, depth - 1, -alpha - 1, -alpha,
                            false, m);
                        needsFullSearch = score > alpha && score < beta;
                    }
                }

                if (needsFullSearch)
                {
                    // Full search
                    score = -NegaMaxSearch(killers, counters, history, depthFromRoot + 1, depth - 1, -beta, -alpha, false,
                        m);
                }

            // Revert the move
            Board.PartialUnApply(m, originalHash, oldEnpassant, inCheck, prevCastleRights, prevFiftyMoveCounter);

            Board.Evaluator.WhiteMirrored = whiteMirrored;
            Board.Evaluator.BlackMirrored = blackMirrored;
            Board.Evaluator.ShouldWhiteMirrored = shouldWhiteMirrored;
            Board.Evaluator.ShouldBlackMirrored = shouldBlackMirrored;
            NnueEvaluator.SimdCopy(Board.Evaluator.WhiteAccumulator, whiteAccPtr);
            NnueEvaluator.SimdCopy(Board.Evaluator.BlackAccumulator, blackAccPtr);

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
                TranspositionTableExtensions.Set(_transpositionTable, TtMask, Board.Hash, (byte)depth, depthFromRoot, score,
                    TranspositionTableFlag.Beta,
                    bestMove);

                if (m.IsQuiet())
                {
                    // Update move ordering heuristics for quiet moves that lead to a beta cut off

                    // History
                    history.UpdateMovesHistory(moves, moveIndex, m, depth);

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

                if (depthFromRoot > 0)
                {
                    // Ensure position is popped from repetition table before returning
                    RepetitionTable.TryPop();
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

        if (depthFromRoot > 0)
        {
            // Ensure position is popped from repetition table before returning
            RepetitionTable.TryPop();
        }

        if (searchedMoves == 0)
        {
            // No available moves, either stalemate or checkmate
            var eval = MoveScoring.EvaluateFinalPosition(depthFromRoot, inCheck);

            TranspositionTableExtensions.Set(_transpositionTable, TtMask, Board.Hash, (byte)depth, depthFromRoot, eval,
                TranspositionTableFlag.Exact);

            return eval;
        }

        // Cache in transposition table
        TranspositionTableExtensions.Set(_transpositionTable, TtMask, Board.Hash, (byte)depth, depthFromRoot, alpha,
            evaluationBound, bestMove);

        return alpha;
    }
}