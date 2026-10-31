using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Search;

/// <summary>
/// Root ranking for human-like difficulty (HumanPlay). A SEPARATE routine from the normal search:
/// only invoked when HumanPlay is enabled, so the NegaMax/Quiescence kernels and the native/CLI
/// search path are completely unaffected.
///
/// Searches every legal root move with a full window at a fixed depth for comparable exact scores,
/// returns them best-first, and attaches move flags used for engine diagnostics:
///   bit0 capture, bit1 castle, bit2 promotion, bit3 gives-check, bits4-6 piece kind (0=P..5=Q,6=K).
/// </summary>
public unsafe partial class Searcher
{
    public List<(uint Move, int Score, int Feat)> RankRootMoves(GameState inputBoard, int depth, int timeLimitMs = 0)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            depth = Math.Clamp(depth, 1, Constants.MaxSearchDepth);

            ResetCore(inputBoard);
            _nodeLimit = 0;
            SetTimeLimits(timeLimitMs, timeLimitMs);
            _sliceActive = false;
            _sliceExhausted = false;

            ref var rootBoard = ref Boards[0];
            inputBoard.Board.CloneTo(ref rootBoard);
            FillInitialAccumulators(Boards, Accumulators);

            var moves = stackalloc uint[218];
            var count = rootBoard.GeneratePseudoLegalMoves(moves, false);

            var ranked = new List<(uint, int, int)>(count);
            var whiteToMove = rootBoard.WhiteToMove;
            var oldEnpassantHashFile = Zobrist.GetEnPassantHashFile(ref rootBoard);
            var newBoardState = Boards + 1;
            var newAccumulatorState = Accumulators + 1;
            var nextHashHistoryEntry = HashHistory + rootBoard.TurnCount;

            for (var i = 0; i < count; i++)
            {
                var m = *(moves + i);

                Unsafe.CopyBlock(newBoardState, Boards, BoardStateData.BoardStateSize);
                if (whiteToMove ? !newBoardState->PartialApplyWhite(m) : !newBoardState->PartialApplyBlack(m))
                    continue;

                newBoardState->UpdateCheckStatus();
                newAccumulatorState->UpdateToParent(Accumulators, newBoardState);
                if (whiteToMove)
                    newBoardState->FinishApplyWhite(ref *newAccumulatorState, m, rootBoard.EnPassantFile, oldEnpassantHashFile, rootBoard.CastleRights);
                else
                    newBoardState->FinishApplyBlack(ref *newAccumulatorState, m, rootBoard.EnPassantFile, oldEnpassantHashFile, rootBoard.CastleRights);
                *nextHashHistoryEntry = newBoardState->Hash;

                var score = -NegaMaxSearch(newBoardState, newAccumulatorState, 1, depth - 1,
                    Constants.MinScore, Constants.MaxScore);
                if (Interrupted())
                    break;

                var feat = (m.IsCapture() ? 1 : 0)
                         | (m.IsCastle() ? 2 : 0)
                         | (m.IsPromotion() ? 4 : 0)
                         | (newBoardState->InCheck ? 8 : 0)
                         | (PieceKind(m.GetMovedPiece()) << 4);
                ranked.Add((m, score, feat));
            }

            ranked.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return ranked;
        }
    }

    // Piece kind index for the human prior features: 0=P 1=N 2=B 3=R 4=Q 5=K.
    private static int PieceKind(byte piece) => piece switch
    {
        Constants.BlackPawn or Constants.WhitePawn => 0,
        Constants.BlackKnight or Constants.WhiteKnight => 1,
        Constants.BlackBishop or Constants.WhiteBishop => 2,
        Constants.BlackRook or Constants.WhiteRook => 3,
        Constants.BlackQueen or Constants.WhiteQueen => 4,
        _ => 5,
    };
}
