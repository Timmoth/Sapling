using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;
using Sapling.Engine.Tuning;

namespace Sapling.Engine.Search;

public partial class Searcher
{
    public unsafe int QuiescenceSearch(BoardStateData* boardState, AccumulatorState* accumulatorState, int depthFromRoot, int alpha, int beta)
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
            return Evaluate(boardState, accumulatorState, depthFromRoot);
        }

        if (boardState->InsufficientMatingMaterial())
        {
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
        ref var ttEntry = ref *(Transpositions + (pHash & TtMask));
        uint ttBestMove = default;

        if (pHash == ttEntry.FullHash)
        {
            ttBestMove = ttEntry.Move;
            var score = TranspositionTableExtensions.RecalculateMateScores(ttEntry.Evaluation, depthFromRoot);

            var transpositionEvaluation = ttEntry.Flag switch
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
        if (!inCheck)
        {
            var pawnChIndex = CorrectionIndex(boardState->PawnHash, boardState->WhiteToMove);
            var whiteMaterialChIndex = CorrectionIndex(boardState->WhiteMaterialHash, boardState->WhiteToMove);
            var blackMaterialChIndex = CorrectionIndex(boardState->BlackMaterialHash, boardState->WhiteToMove);

            // Evaluate current position
            var val = AdjustEval(pawnChIndex, whiteMaterialChIndex, blackMaterialChIndex, Evaluate(boardState, accumulatorState, depthFromRoot));
            if (val >= beta)
            {
                // Beta cut off
                return val;
            }

            alpha = int.Max(alpha, val);
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
                ttEntry.Set(pHash, 0, depthFromRoot, finalEval,
                    TranspositionTableFlag.Exact);
                return finalEval;
            }

            ttEntry.Set(pHash, 0, depthFromRoot, alpha,
                TranspositionTableFlag.Alpha,
                default);
            return alpha;
        }

        var scores = stackalloc int[psuedoMoveCount];

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

        var captures = stackalloc short[boardState->PieceCount];

        for (var i = 0; i < psuedoMoveCount; ++i)
        {
            *(scores + i) = boardState->ScoreMoveQuiescence(occupancyBitBoards, captures, *(moves + i), ttBestMove);
        }

        var evaluationBound = TranspositionTableFlag.Alpha;

        uint bestMove = default;
        var hasValidMove = false;

        var nextAccumulatorState = accumulatorState + 1;
        var nextBoardState = boardState + 1;

        var nextHashHistoryEntry = HashHistory + boardState->TurnCount;
        var prevEnpassant = boardState->EnPassantFile;
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

            Unsafe.CopyBlock(nextBoardState, boardState, BoardStateData.BoardStateSize);

            var m = *currentMovePtr;

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
                nextBoardState->FinishApplyWhite(ref *nextAccumulatorState, m, prevEnpassant, prevCastleRights);
            }
            else
            {
                nextBoardState->FinishApplyBlack(ref *nextAccumulatorState, m, prevEnpassant, prevCastleRights);
            }

            *nextHashHistoryEntry = nextBoardState->Hash;

            Sse.Prefetch0(Transpositions + (nextBoardState->Hash & TtMask));

            var val = -QuiescenceSearch(nextBoardState, nextAccumulatorState, depthFromRoot + 1, -beta, -alpha);

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
                ttEntry.Set(pHash, 0, depthFromRoot, val,
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
            ttEntry.Set(pHash, 0, depthFromRoot, finalEval,
                TranspositionTableFlag.Exact);
            return finalEval;
        }

        // Cache in transposition table
        ttEntry.Set(pHash, 0, depthFromRoot, alpha,
            evaluationBound,
            bestMove);

        return alpha;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void ShiftPvMoves(int target, int source, int moveCountToCopy)
    {
        // Check if the source position is zero
        if (*(_pVTable + source) == 0)
        {
            // Calculate the number of bytes to clear
            var bytesToClear = _pvTableBytes - (nuint)target * sizeof(uint);
            NativeMemory.Clear(_pVTable + target, bytesToClear);
            return;
        }
        
        Unsafe.CopyBlock(_pVTable + target, _pVTable + source, (uint)(moveCountToCopy * sizeof(uint)));
    }
}