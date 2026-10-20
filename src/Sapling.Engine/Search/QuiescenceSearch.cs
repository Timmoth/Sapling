using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;
using Sapling.Engine.Evaluation;

namespace Sapling.Engine.Search;

public partial class Searcher
{
    public unsafe int QuiescenceSearch(BoardStateData* boardState, AccumulatorState* accumulatorState, int depthFromRoot, int alpha, int beta)
    {
        NodesVisited++;

        if (ShouldStop())
        {
            // Search was cancelled
            return 0;
        }

        if (depthFromRoot >= Constants.MaxSearchDepth || boardState->TurnCount == ushort.MaxValue)
        {
            // Reached max depth
            return Evaluate(boardState, accumulatorState, depthFromRoot);
        }

        var isDraw = boardState->HalfMoveClock >= 100 ||
            boardState->InsufficientMatingMaterial() ||
            RepetitionDetector.IsThreefoldRepetition(boardState->TurnCount, boardState->HalfMoveClock, HashHistory);
        if (isDraw)
        {
            if (boardState->InCheck && !boardState->HasAnyLegalMove())
                return MoveScoring.EvaluateFinalPosition(depthFromRoot, true);
            // Detect draw by Fifty move counter or repetition
            return 0;
        }

        if (alpha < 0 && boardState->HasRepetition(HashHistory, depthFromRoot))
        {
            alpha = 0;
            if (alpha >= beta)
                return alpha;
        }

        var pHash = boardState->Hash;
        uint ttBestMove = default;

        var ttSnapshot = TranspositionTableExtensions.ProbeCluster(Transpositions, pHash, TtMask,
            _ttGeneration, out _, SharedTranspositions);
        if (ttSnapshot.Valid && pHash == ttSnapshot.FullHash)
        {
            ttBestMove = ttSnapshot.Move;
            var score = TranspositionTableExtensions.RecalculateMateScores(ttSnapshot.Evaluation, depthFromRoot);

            var transpositionEvaluation = ttSnapshot.Flag switch
            {
                TranspositionTableFlag.Exact => score,
                TranspositionTableFlag.Alpha when score <= alpha => alpha,
                TranspositionTableFlag.Beta when score >= beta => beta,
                _ => TranspositionTableExtensions.NoHashEntry
            };
            if (transpositionEvaluation != TranspositionTableExtensions.NoHashEntry)
            {
                // Transposition table hit
                return transpositionEvaluation;
            }
        }

        var inCheck = boardState->InCheck;
        int qEval = 0;
        if (!inCheck)
        {
            var pawnChIndex = CorrectionIndex(boardState->PawnHash, boardState->WhiteToMove);
            var whiteMaterialChIndex = CorrectionIndex(boardState->WhiteMaterialHash, boardState->WhiteToMove);
            var blackMaterialChIndex = CorrectionIndex(boardState->BlackMaterialHash, boardState->WhiteToMove);
            var previousMoveIndex = accumulatorState->Move == default
                ? 0
                : accumulatorState->Move.GetCounterMoveIndex();

            // Evaluate current position
            qEval = AdjustEval(pawnChIndex, whiteMaterialChIndex, blackMaterialChIndex, previousMoveIndex,
                Evaluate(boardState, accumulatorState, depthFromRoot));
            if (qEval >= beta)
            {
                if (!boardState->HasAnyLegalMove())
                    return 0;
                // Beta cut off
                return qEval;
            }

            alpha = int.Max(alpha, qEval);
        }

        // Get all capturing moves
        var moves = stackalloc uint[218];
        var psuedoMoveCount = boardState->GeneratePseudoLegalMoves(moves, !inCheck);

        if (psuedoMoveCount == 0)
        {
            if (inCheck)
            {
                // No move could be played, either stalemate or checkmate
                var finalEval = MoveScoring.EvaluateFinalPosition(depthFromRoot, inCheck);

                // Cache in transposition table
                StoreTransposition(pHash, 0, depthFromRoot, finalEval,
                    TranspositionTableFlag.Exact);
                return finalEval;
            }

            if (!boardState->HasAnyLegalMove())
            {
                StoreTransposition(pHash, 0, depthFromRoot, 0,
                    TranspositionTableFlag.Exact);
                return 0;
            }

            StoreTransposition(pHash, 0, depthFromRoot, alpha,
                TranspositionTableFlag.Alpha,
                default);
            return alpha;
        }

        var scores = stackalloc int[MaxMovesPerPly];

        var occupancyBitBoards = stackalloc ulong[8]
        {
            boardState->Occupancy[Constants.WhitePieces],
            boardState->Occupancy[Constants.BlackPieces],
            boardState->Occupancy[Constants.BlackPawn] | boardState->Occupancy[Constants.WhitePawn],
            boardState->Occupancy[Constants.BlackKnight] | boardState->Occupancy[Constants.WhiteKnight],
            boardState->Occupancy[Constants.BlackBishop] | boardState->Occupancy[Constants.WhiteBishop],
            boardState->Occupancy[Constants.BlackRook] | boardState->Occupancy[Constants.WhiteRook],
            boardState->Occupancy[Constants.BlackQueen] | boardState->Occupancy[Constants.WhiteQueen],
            boardState->Occupancy[Constants.BlackKing] | boardState->Occupancy[Constants.WhiteKing]
        };

        var captures = stackalloc short[64];

        for (var i = 0; i < psuedoMoveCount; ++i)
        {
            var move = *(moves + i);
            *(scores + i) = ScoreQuiescenceMove(ref *boardState, move, ttBestMove,
                occupancyBitBoards, captures, History);
        }

        var evaluationBound = TranspositionTableFlag.Alpha;

        uint bestMove = default;
        var hasValidMove = false;

        var nextAccumulatorState = accumulatorState + 1;
        var nextBoardState = boardState + 1;

        var nextHashHistoryEntry = HashHistory + boardState->TurnCount;
        var prevEnpassant = boardState->EnPassantFile;
        var prevEnpassantHashFile = Zobrist.GetEnPassantHashFile(ref *boardState);
        var prevCastleRights = boardState->CastleRights;
        var whiteToMove = boardState->WhiteToMove;
        for (var moveIndex = 0; moveIndex < psuedoMoveCount; ++moveIndex)
        {
            // Incremental move sorting
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
                    var tempScore = *currentScorePtr;
                    var tempMove = *currentMovePtr;

                    *currentScorePtr = *compareScorePtr;
                    *currentMovePtr = *compareMovePtr;

                    *compareScorePtr = tempScore;
                    *compareMovePtr = tempMove;
                }
            }

            var m = *currentMovePtr;

            Unsafe.CopyBlock(nextBoardState, boardState, BoardStateData.BoardStateSize);

            if (whiteToMove ? !nextBoardState->PartialApplyWhite(m) : !nextBoardState->PartialApplyBlack(m))
            {
                // illegal move
                continue;
            }

            nextBoardState->UpdateCheckStatus();

            hasValidMove = true;

            if (!inCheck && !nextBoardState->InCheck && (*currentScorePtr) < SpsaOptions.InterestingQuiescenceMoveScore)
            {
                //skip playing bad captures when not in check
                continue;
            }

            nextAccumulatorState->UpdateToParent(accumulatorState, nextBoardState);

            if (whiteToMove)
            {
                nextBoardState->FinishApplyWhite(ref *nextAccumulatorState, m, prevEnpassant, prevEnpassantHashFile, prevCastleRights);
            }
            else
            {
                nextBoardState->FinishApplyBlack(ref *nextAccumulatorState, m, prevEnpassant, prevEnpassantHashFile, prevCastleRights);
            }

            *nextHashHistoryEntry = nextBoardState->Hash;

            PrefetchTransposition(Transpositions + TranspositionTableExtensions.ClusterStart(nextBoardState->Hash, TtMask));

            var val = -QuiescenceSearch(nextBoardState, nextAccumulatorState, depthFromRoot + 1, -beta, -alpha);

            if (ShouldStop())
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
                StoreTransposition(pHash, 0, depthFromRoot, val,
                    TranspositionTableFlag.Beta, bestMove);
                // Beta cut off
                return val;
            }
        }

        if (ShouldStop())
        {
            // Search was cancelled
            return 0;
        }

        if (!hasValidMove && inCheck)
        {
            // No move could be played, either stalemate or checkmate
            var finalEval = MoveScoring.EvaluateFinalPosition(depthFromRoot, inCheck);

            // Cache in transposition table
            StoreTransposition(pHash, 0, depthFromRoot, finalEval,
                TranspositionTableFlag.Exact);
            return finalEval;
        }

        // In a non-check node the generated move list contains captures only. A
        // stalemated side can still have pseudo-legal captures (for example, a
        // pinned piece capturing an adjacent piece), all of which are rejected
        // by PartialApply. Distinguish that from an ordinary quiet position.
        if (!inCheck && !hasValidMove && !boardState->HasAnyLegalMove())
        {
            StoreTransposition(pHash, 0, depthFromRoot, 0,
                TranspositionTableFlag.Exact);
            return 0;
        }

        // Cache in transposition table
        StoreTransposition(pHash, 0, depthFromRoot, alpha,
            evaluationBound,
            bestMove);

        return alpha;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe int ScoreQuiescenceMove(ref BoardStateData boardState, uint move,
        uint ttBestMove, ulong* occupancyBitBoards, short* captures, int* history)
    {
        if (move == ttBestMove)
            return SpsaOptions.MoveOrderingBestMoveBias;

        if (move.IsCapture())
        {
            if (move.IsEnPassant())
                return SpsaOptions.MoveOrderingEnPassantMoveBias;

            var captureDelta = boardState.StaticExchangeEvaluation(occupancyBitBoards, captures, move);
            return (captureDelta >= 0
                    ? SpsaOptions.MoveOrderingWinningCaptureBias
                    : SpsaOptions.MoveOrderingLosingCaptureBias) + captureDelta +
                   (move.IsPromotion() ? SpsaOptions.MoveOrderingCapturePromoteBias : 0);
        }

        if (move.IsPromotion())
            return SpsaOptions.MoveOrderingPromoteBias;

        return *(history + move.GetCounterMoveIndex());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void ShiftPvMoves(int target, int source, int moveCountToCopy)
    {
        // Nothing to copy at the deepest ply: return before touching the source, which can sit at
        // the end of the PV allocation (index == length) and would otherwise be an out-of-bounds read.
        if (moveCountToCopy <= 0)
            return;

        // Check if the source position is zero
        if (*(_pVTable + source) == 0)
        {
            // Empty child: terminate this node's PV by clearing only the destination row's
            // remainder (moveCountToCopy slots), not the whole triangular table tail. Readers
            // stop at the first zero, so zeroing beyond this row is unnecessary work.
            NativeMemory.Clear(_pVTable + target, (nuint)moveCountToCopy * sizeof(uint));
            return;
        }

        Unsafe.CopyBlock(_pVTable + target, _pVTable + source, (uint)(moveCountToCopy * sizeof(uint)));
    }
}
