using BenchmarkDotNet.Attributes;
using Sapling.Engine;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;

namespace Sapling.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 8)]
public unsafe class NnueRefreshBench
{
    [Params(0, 1, 4, 16)]
    public int ChangedPawns { get; set; }
    private Searcher _searcher = null!;
    private Transposition* _tt;
    private BoardStateData _a;
    private BoardStateData _b;
    private int _bucketIndex;

    [GlobalSetup]
    public void Setup()
    {
        _tt = MemoryHelpers.Allocate<Transposition>(1024);
        _searcher = new Searcher(_tt, 1024);
        _a = BoardStateExtensions.CreateBoardFromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        // Construct a second fixed position by shifting selected pawns one rank.
        // Hashes are irrelevant to these direct accumulator refresh calls.
        _b = _a;
        for (var i = 0; i < ChangedPawns; i++)
        {
            var piece = i < 8 ? Constants.WhitePawn : Constants.BlackPawn;
            var from = i < 8 ? 8 + i : 48 + i - 8;
            var to = i < 8 ? from + 8 : from - 8;
            var mask = (1UL << from) | (1UL << to);
            _b.Occupancy[piece] ^= mask;
            _b.Occupancy[i < 8 ? Constants.WhitePieces : Constants.BlackPieces] ^= mask;
            _b.Occupancy[Constants.Occupancy] ^= mask;
        }
        _searcher.Boards[0] = _a;
        _searcher.RefreshAccumulatorWhite(_searcher.Boards, _searcher.Accumulators, 0);
        var state = _searcher.Accumulators;
        _bucketIndex = (state->WhiteInputBucket << 1) + (state->WhiteMirrored ? 1 : 0);
    }

    [Benchmark]
    public short CachedRefreshPair()
    {
        _searcher.Boards[0] = _b;
        _searcher.RefreshAccumulatorWhite(_searcher.Boards, _searcher.Accumulators, 0);
        _searcher.Boards[0] = _a;
        _searcher.RefreshAccumulatorWhite(_searcher.Boards, _searcher.Accumulators, 0);
        return _searcher.WhiteAccumulators[0][0];
    }

    [Benchmark]
    public short ColdRefresh()
    {
        _searcher.BucketCacheWhiteBoards[_bucketIndex].PieceCount = 0;
        _searcher.Boards[0] = _b;
        _searcher.RefreshAccumulatorWhite(_searcher.Boards, _searcher.Accumulators, 0);
        return _searcher.WhiteAccumulators[0][0];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher.Dispose();
        MemoryHelpers.FreeAligned(_tt);
    }
}
