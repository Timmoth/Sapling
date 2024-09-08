using iBlunder.Engine.MoveGen;
using iBlunder.Engine.Transpositions;

namespace iBlunder.Engine.Search;

public unsafe partial class Searcher
{
    public const ulong TtMask = 0b1111_1111_1111_1111_1111_1111;

    private static readonly int[] AsperationWindows = { 40, 100, 300, 900, 2700, Constants.MaxScore };
    private readonly Transposition* _transpositionTable;
    public readonly RepetitionTable RepetitionTable = new();
    public readonly Transposition[] Transpositions;

    private long _lockedUntil;

    private bool _searchCancelled;
    public uint BestOpponentMove;
    public int BestScoreSoFar = int.MinValue;

    public uint BestSoFar;
    public BoardState Board = default!;
    public int NodesVisited;

    public Searcher(Transposition[] transpositions)
    {
        Transpositions = transpositions;
        fixed (Transposition* p = transpositions)
        {
            _transpositionTable = p; // Store the pointer
        }
    }

    public void Stop()
    {
        _searchCancelled = true;
    }

    public (uint move, int depthSearched, int score, uint ponder, int nodes) Search(int nodeLimit = 0,
        int depthLimit = 0)
    {
        BestSoFar = default;
        BestOpponentMove = default;
        BestScoreSoFar = int.MinValue;

        NodesVisited = 0;

        var depthSearched = 0;
        _searchCancelled = false;

        Span<uint> killers = stackalloc uint[Constants.MaxSearchDepth * 2];
        Span<int> history = stackalloc int[13 * 64];
        Span<uint> counters = stackalloc uint[13 * 64];

        var alpha = Constants.MinScore;
        var beta = Constants.MaxScore;
        var lastIterationEval = 0;

        var maxDepth = depthLimit > 0 ? depthLimit : Constants.MaxSearchDepth;

        for (var j = 1; j <= maxDepth; j++)
        {
            if (_searchCancelled)
            {
                break;
            }

            if (j <= 1)
            {
                RepetitionTable.Init(Board.RepetitionPositionHistory);
                killers.Clear();
                lastIterationEval = NegaMaxSearch(killers, counters, history, 0, j, alpha, beta, false);
            }
            else
            {
                var alphaWindowIndex = 0;
                var betaWindowIndex = 0;
                do
                {
                    alpha = alphaWindowIndex >= 5
                        ? Constants.MinScore
                        : lastIterationEval - AsperationWindows[alphaWindowIndex];
                    beta = betaWindowIndex >= 5
                        ? Constants.MaxScore
                        : lastIterationEval + AsperationWindows[betaWindowIndex];

                    RepetitionTable.Init(Board.RepetitionPositionHistory);
                    killers.Clear();

                    var eval = NegaMaxSearch(killers, counters, history, 0, j, alpha, beta, false);

                    if (eval <= alpha)
                    {
                        ++alphaWindowIndex;
                    }
                    else if (eval >= beta)
                    {
                        ++betaWindowIndex;
                    }
                    else
                    {
                        lastIterationEval = eval;
                        break;
                    }

                    if (nodeLimit > 0 && NodesVisited > nodeLimit)
                    {
                        break;
                    }
                } while (true);
            }

            if (nodeLimit > 0 && NodesVisited > nodeLimit)
            {
                break;
            }

            depthSearched = j;
        }

        if (BestSoFar != default)
        {
            return (BestSoFar, depthSearched, BestScoreSoFar, BestOpponentMove, NodesVisited);
        }

        // If couldn't find a good move, due to timeout return first legal move after move ordering
        Span<uint> moves = stackalloc uint[218];
        var psuedoMoveCount = Board.GeneratePseudoLegalMoves(moves, false);

        var originalHash = Board.Hash;
        var oldEnpassant = Board.EnPassantFile;
        var prevInCheck = Board.InCheck;

        var prevCastleRights = Board.CastleRights;
        var prevFiftyMoveCounter = Board.HalfMoveClock;

        // Get killer move
        var killerA = killers[0 * 2];
        var killerB = killers[0 * 2 + 1];

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
            scores[i] = Board.ScoreMove(history, occupancyBitBoards, captures, moves[i], killerA, killerB, default,
                default);
        }

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
            Board.FinishApply(m, oldEnpassant, prevCastleRights);

            BestSoFar = m;
            BestScoreSoFar = Board.Evaluate();

            Board.PartialUnApply(m, originalHash, oldEnpassant, prevInCheck, prevCastleRights, prevFiftyMoveCounter);
            Board.FinishUnApplyMove(m, oldEnpassant);
            break;
        }

        return (BestSoFar, depthSearched, BestScoreSoFar, BestOpponentMove, NodesVisited);
    }

    public (uint move, int depthSearched, int score, uint ponder, int nodes) DepthBoundSearch(int depth)
    {
        BestSoFar = default;
        BestOpponentMove = default;
        BestScoreSoFar = int.MinValue;

        NodesVisited = 0;

        var depthSearched = 0;
        _searchCancelled = false;

        var alpha = Constants.MinScore;
        var beta = Constants.MaxScore;
        var lastIterationEval = 0;

        Span<uint> killers = stackalloc uint[Constants.MaxSearchDepth * 2];
        Span<int> history = stackalloc int[13 * 64];
        Span<uint> counters = stackalloc uint[13 * 64];

        for (var j = 1; j <= depth; j++)
        {
            if (_searchCancelled)
            {
                break;
            }

            if (j <= 1)
            {
                RepetitionTable.Init(Board.RepetitionPositionHistory);
                lastIterationEval = NegaMaxSearch(killers, counters, history, 0, j, alpha, beta, false);
            }
            else
            {
                var alphaWindowIndex = 0;
                var betaWindowIndex = 0;
                do
                {
                    if (alphaWindowIndex >= 5)
                    {
                        alpha = Constants.MinScore;
                    }
                    else
                    {
                        alpha = lastIterationEval - AsperationWindows[alphaWindowIndex];
                    }

                    if (betaWindowIndex >= 5)
                    {
                        beta = Constants.MaxScore;
                    }
                    else
                    {
                        beta = lastIterationEval + AsperationWindows[betaWindowIndex];
                    }

                    RepetitionTable.Init(Board.RepetitionPositionHistory);
                    var eval = NegaMaxSearch(killers, counters, history, 0, j, alpha, beta, false);

                    if (eval <= alpha)
                    {
                        ++alphaWindowIndex;
                    }
                    else if (eval >= beta)
                    {
                        ++betaWindowIndex;
                    }
                    else
                    {
                        lastIterationEval = eval;
                        break;
                    }
                } while (true);
            }


            depthSearched = j;
        }

        return (BestSoFar, depthSearched, BestScoreSoFar, BestOpponentMove, NodesVisited);
    }

    public void Init(long currentUnixSeconds, BoardState board)
    {
        // Cancels active search
        _searchCancelled = true;
        Board = board;
        _lockedUntil = currentUnixSeconds + 60;
    }

    public void Release()
    {
        _lockedUntil = 0;
    }

    public bool IsBusy(long currentUnixSeconds)
    {
        return _lockedUntil > currentUnixSeconds;
    }
}