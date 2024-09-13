using Sapling.Engine.Evaluation;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

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
public unsafe partial class Searcher
{
    public readonly uint TtMask;
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

        TtMask = (uint)transpositions.Length - 1;
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

        for (var j = 0; j <= maxDepth; j++)
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