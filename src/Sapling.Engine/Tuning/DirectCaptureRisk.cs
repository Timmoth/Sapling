using Sapling.Engine.MoveGen;

namespace Sapling.Engine.Tuning;

/// <summary>Detects immediate legal captures of a minor-or-better piece without a legal recapture.</summary>
public static unsafe class DirectCaptureRisk
{
    public static bool IsRiskAfterMove(ref BoardStateData source, uint move)
    {
        return IsRiskAfterMove(ref source, move, new List<uint>(32), new List<uint>(32));
    }

    public static bool IsRiskAfterMove(ref BoardStateData source, uint move,
        List<uint> replies, List<uint> recaptures)
    {
        ArgumentNullException.ThrowIfNull(replies);
        ArgumentNullException.ThrowIfNull(recaptures);
        var after = source;
        ApplyMove(ref after, move);
        // We only need captures here; avoiding quiet replies substantially reduces the
        // per-candidate cost of the runtime safety signal.
        after.GenerateLegalMoves(replies, captureOnly: true);
        foreach (var capture in replies)
        {
            var victim = capture.GetCapturedPiece();
            if (victim == Constants.None || PieceValues.PieceValue[victim] < Constants.KnightValue)
                continue;

            var captureSquare = capture.GetToSquare();
            var afterCapture = after;
            ApplyMove(ref afterCapture, capture);
            var capturingPieceAfterMove = afterCapture.GetPiece(captureSquare);
            afterCapture.GenerateLegalMoves(recaptures, captureOnly: true);
            var recaptured = recaptures.Any(reply => reply.GetToSquare() == captureSquare
                && reply.GetCapturedPiece() == capturingPieceAfterMove);
            if (!recaptured)
                return true;
        }
        return false;
    }

    private static void ApplyMove(ref BoardStateData board, uint move)
    {
        var oldEnpassant = board.EnPassantFile;
        var oldEnpassantHashFile = Zobrist.GetEnPassantHashFile(ref board);
        var oldCastle = board.CastleRights;
        var whiteToMove = board.WhiteToMove;
        if (whiteToMove) board.PartialApplyWhite(move);
        else board.PartialApplyBlack(move);
        board.UpdateCheckStatus();

        AccumulatorState accumulator = default;
        fixed (BoardStateData* boardPtr = &board)
        {
            if (whiteToMove)
                boardPtr->FinishApplyWhite(ref accumulator, move, oldEnpassant, oldEnpassantHashFile, oldCastle);
            else
                boardPtr->FinishApplyBlack(ref accumulator, move, oldEnpassant, oldEnpassantHashFile, oldCastle);
        }
    }
}
