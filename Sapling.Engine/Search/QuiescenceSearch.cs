using System.Reflection.Metadata;
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
            return Board.Evaluate();
        }

        var pvIndex = PVTable.Indexes[depthFromRoot];
        var nextPvIndex = PVTable.Indexes[depthFromRoot + 1];
        _pVTable[pvIndex] = 0;

        if (Board.InsufficientMatingMaterial())
        {
            // Detect draw by Fifty move counter or repetition
            return 0;
        }

        var ttProbeResult =
            TranspositionTableExtensions.Get(_transpositionTable, TtMask, Board.Hash, 0, depthFromRoot, alpha, beta);
        if (ttProbeResult.Evaluation != TranspositionTableExtensions.NoHashEntry)
        {
            // Transposition table hit
            return ttProbeResult.Evaluation;
        }

        var inCheck = Board.InCheck;
        if (!inCheck)
        {
            // Evaluate current position
            var val = Board.Evaluate();
            if (val >= beta)
            {
                // Beta cut off
                return val;
            }

            alpha = int.Max(alpha, val);
        }

        // Get all capturing moves
        Span<uint> moves = stackalloc uint[218];
        var psuedoMoveCount = Board.GeneratePseudoLegalMoves(moves, !inCheck);

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
            scores[i] = Board.ScoreMoveQuiescence(occupancyBitBoards, captures, moves[i], ttProbeResult.BestMove);
        }

        var originalHash = Board.Hash;
        var oldEnpassant = Board.EnPassantFile;
        var prevInCheck = Board.InCheck;

        var prevCastleRights = Board.CastleRights;
        var prevFiftyMoveCounter = Board.HalfMoveClock;
        var evaluationBound = TranspositionTableFlag.Alpha;

        uint bestMove = default;
        var hasValidMove = false;
        var shouldWhiteMirrored = Board.Evaluator.ShouldWhiteMirrored;
        var shouldBlackMirrored = Board.Evaluator.ShouldBlackMirrored;
        var whiteMirrored = Board.Evaluator.WhiteMirrored;
        var blackMirrored = Board.Evaluator.BlackMirrored;
        var whiteAccPtr = stackalloc VectorShort[NnueEvaluator.AccumulatorSize];
        var blackAccPtr = stackalloc VectorShort[NnueEvaluator.AccumulatorSize];
        NnueEvaluator.SimdCopy(whiteAccPtr, Board.Evaluator.WhiteAccumulator);
        NnueEvaluator.SimdCopy(blackAccPtr, Board.Evaluator.BlackAccumulator);

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
                        // illegal move
                        Board.PartialUnApply(m, originalHash, oldEnpassant, prevInCheck, prevCastleRights,
                            prevFiftyMoveCounter);
                        continue;
                    }

                    Board.UpdateCheckStatus();

                    if (!prevInCheck && !Board.InCheck && scores[moveIndex] < Constants.LosingCaptureBias)
                    {
                        //skip playing bad captures when not in check
                        Board.PartialUnApply(m, originalHash, oldEnpassant, prevInCheck, prevCastleRights,
                            prevFiftyMoveCounter);
                        continue;
                    }

                    hasValidMove = true;

                    Board.FinishApply(m, oldEnpassant, prevCastleRights);

                    Sse.Prefetch0(_transpositionTable + (Board.Hash & TtMask));

                    var val = -QuiescenceSearch(depthFromRoot + 1, -beta, -alpha);

                    Board.PartialUnApply(m, originalHash, oldEnpassant, prevInCheck, prevCastleRights,
                        prevFiftyMoveCounter);

                    Board.Evaluator.WhiteMirrored = whiteMirrored;
                    Board.Evaluator.BlackMirrored = blackMirrored;
                    Board.Evaluator.ShouldWhiteMirrored = shouldWhiteMirrored;
                    Board.Evaluator.ShouldBlackMirrored = shouldBlackMirrored;
                    NnueEvaluator.SimdCopy(Board.Evaluator.WhiteAccumulator, whiteAccPtr);
                    NnueEvaluator.SimdCopy(Board.Evaluator.BlackAccumulator, blackAccPtr);

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
                        TranspositionTableExtensions.Set(_transpositionTable, TtMask, Board.Hash, 0, depthFromRoot, val,
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
            TranspositionTableExtensions.Set(_transpositionTable, TtMask, Board.Hash, 0, depthFromRoot, finalEval,
                TranspositionTableFlag.Exact);
            return finalEval;
        }

        // Cache in transposition table
        TranspositionTableExtensions.Set(_transpositionTable, TtMask, Board.Hash, 0, depthFromRoot, alpha,
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