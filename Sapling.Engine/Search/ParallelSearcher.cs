using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Search;

public unsafe class ParallelSearcher
{
    public readonly List<Searcher> Searchers = new();
    public readonly Transposition* Transpositions;
    public readonly int TTSize;

    // Used to prevent a previous searches timeout cancelling a new search
    private Guid _prevSearchId = Guid.NewGuid();

    public ParallelSearcher(int ttSize)
    {
        TTSize = ttSize;
        Transpositions = AllocateTranspositions((nuint)ttSize);

        // Default to one thread
        Searchers.Add(new Searcher(Transpositions, ttSize));
    }

    ~ParallelSearcher()
    {
        NativeMemory.AlignedFree(Transpositions);
    }
    public static unsafe Transposition* AllocateTranspositions(nuint items)
    {
        const nuint alignment = 64;

        nuint bytes = ((nuint)sizeof(Transposition) * (nuint)items);
        void* block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (Transposition*)block;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MoveFromToIndex(uint move)
    {
        return MoveExtensions.BitFieldExtract(move, 4, 6) * 64 +
               MoveExtensions.BitFieldExtract(move, 10, 6);
    }

    public static int ThreadValue(int score, int worstScore, int depth)
    {
        return (score - worstScore) * depth;
    }

    public void Stop()
    {
        foreach (var searcher in Searchers)
        {
            searcher.Stop();
        }
    }

    public (List<uint> pv, int depthSearched, int score, int nodes, TimeSpan duration) NodeBoundSearch(
        GameState state, int nodeLimit = 0, int maxDepth = 0)
    {
        var start = DateTime.Now;
        var searchResult = Searchers[0].Search(state, nodeLimit, maxDepth);
        return (searchResult.pv, searchResult.depthSearched, searchResult.score,
            searchResult.nodes, DateTime.Now - start);
    }

    public (List<uint> pv, int depthSearched, int score, int nodes, TimeSpan duration) TimeBoundSearch(
        GameState state, int thinkTime)
    {
        var newSearchId = Guid.NewGuid();
        _prevSearchId = newSearchId;

        _ = Task.Delay(thinkTime).ContinueWith(t =>
        {
            if (_prevSearchId != newSearchId)
            {
                // Prevent a previous searches timeout cancelling a new search
                return;
            }

            // Stop all searchers once think time has been reached
            foreach (var searcher in Searchers)
            {
                searcher.Stop();
            }
        });

        var start = DateTime.Now;

        if (Searchers.Count == 1)
        {
            var searchResult = Searchers[0].Search(state, writeInfo: true);
            return (searchResult.pv, searchResult.depthSearched, searchResult.score,
                searchResult.nodes, DateTime.Now - start);
        }

        // Thread-local storage for best move in each thread
        var results =
            new ThreadLocal<(List<uint> move, int depthSearched, int score, int nodes)>(
                () => (new List<uint>(), 0, int.MinValue, 0), true);


        // Parallel search, with thread-local best move
        Parallel.For(0, Searchers.Count,
            i => { results.Value = Searchers[i].Search(state, writeInfo: i == 0); });

        var dt = DateTime.Now - start;

        Span<int> voteMap = stackalloc int[64 * 64];
        var worstScore = int.MaxValue;
        var nodes = 0;
        // First pass: Initialize the worst score and reset vote map
        foreach (var result in results.Values)
        {
            worstScore = Math.Min(worstScore, result.score);
            nodes += result.nodes;
        }

        // Second pass: Accumulate votes
        foreach (var result in results.Values)
        {
            voteMap[MoveFromToIndex(result.move[0])] += ThreadValue(result.score, worstScore, result.depthSearched);
        }

        // Initialize best thread and best scores
        var bestMove = results.Values[0].move;
        var bestScore = results.Values[0].score;
        var bestDepth = results.Values[0].depthSearched;
        var bestVoteScore = voteMap[MoveFromToIndex(results.Values[0].move[0])];

        // Find the best thread
        for (var i = 1; i < results.Values.Count; i++)
        {
            var currentVoteScore = voteMap[MoveFromToIndex(results.Values[i].move[0])];
            if (currentVoteScore <= bestVoteScore)
            {
                continue;
            }

            bestMove = results.Values[i].move;
            bestScore = results.Values[i].score;
            bestDepth = results.Values[i].depthSearched;
            bestVoteScore = currentVoteScore;
        }

        return (bestMove, bestDepth, bestScore, nodes, dt);
    }

    public (List<uint> move, int depthSearched, int score, int nodes, TimeSpan duration) DepthBoundSearch(
        GameState state, int depth)
    {
        var searchId = Guid.NewGuid();
        _prevSearchId = searchId;


        // Thread-local storage for best move in each thread
        var results =
            new ThreadLocal<(List<uint> move, int depthSearched, int score, int nodes)>(
                () => (new List<uint>(), 0, int.MinValue, 0), true);

        var start = DateTime.Now;

        // Parallel search, with thread-local best move
        Parallel.For(0, Searchers.Count,
            i => { results.Value = Searchers[i].Search(state, depthLimit: depth, writeInfo: i == 0); });
        var dt = DateTime.Now - start;

        Span<int> voteMap = stackalloc int[64 * 64];
        var worstScore = int.MaxValue;
        var nodes = 0;
        // First pass: Initialize the worst score and reset vote map
        foreach (var result in results.Values)
        {
            worstScore = Math.Min(worstScore, result.score);
            nodes += result.nodes;
        }

        // Second pass: Accumulate votes
        foreach (var result in results.Values)
        {
            voteMap[MoveFromToIndex(result.move[0])] += ThreadValue(result.score, worstScore, result.depthSearched);
        }

        // Initialize best thread and best scores
        var bestMove = results.Values[0].move;
        var bestScore = results.Values[0].score;
        var bestDepth = results.Values[0].depthSearched;
        var bestVoteScore = voteMap[MoveFromToIndex(results.Values[0].move[0])];

        // Find the best thread
        for (var i = 1; i < results.Values.Count; i++)
        {
            var currentVoteScore = voteMap[MoveFromToIndex(results.Values[i].move[0])];
            if (currentVoteScore > bestVoteScore)
            {
                bestMove = results.Values[i].move;
                bestScore = results.Values[i].score;
                bestDepth = results.Values[i].depthSearched;
                bestVoteScore = currentVoteScore;
            }
        }

        return (bestMove, bestDepth, bestScore, nodes, dt);
    }

    public void SetThreads(int searchThreads)
    {
        // Clamp the number of threads to be between 1 and the number of available processor cores
        searchThreads = Math.Clamp(searchThreads, 1, Environment.ProcessorCount);

        // Get the current number of searchers
        var currentSearcherCount = Searchers.Count;

        // If the current number of searchers is equal to the desired number, do nothing
        if (currentSearcherCount == searchThreads)
        {
            return;
        }

        // If there are more searchers than needed, remove the excess ones
        if (currentSearcherCount > searchThreads)
        {
            for (var i = currentSearcherCount - 1; i >= searchThreads; i--)
            {
                Searchers.RemoveAt(i);
            }
        }
        else
        {
            // If there are fewer searchers than needed, add the required number
            for (var i = currentSearcherCount; i < searchThreads; i++)
            {
                Searchers.Add(new Searcher(Transpositions, TTSize));
            }
        }
    }
}