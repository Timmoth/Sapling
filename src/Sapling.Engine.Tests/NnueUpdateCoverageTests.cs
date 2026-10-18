using System;
using Sapling.Engine;
using Sapling.Engine.DataGen;
using Sapling.Engine.MoveGen;
using Sapling.Engine.Evaluation;
using Sapling.Engine.Search;
using Sapling.Engine.Transpositions;
using Xunit;

namespace Sapling.Engine.Tests;

// Takeover priority 5: extend NNUE fixture coverage beyond quiet/capture — promotions,
// castling, en passant, king-bucket crossings, long randomized playouts with full-refresh
// comparison at every ply, and kernel aliasing/equivalence.
public unsafe class NnueUpdateCoverageTests
{
    // Positions chosen so random playouts actually reach each special move class and
    // king-bucket transitions.
    private static readonly (string fen, string why)[] PlayoutCorpus =
    {
        ("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1", "promotion race"),
        ("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "castling available both sides"),
        ("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", "en passant available"),
        ("4k3/8/8/8/8/8/8/4K3 w - - 0 1", "king-only walk crosses input buckets"),
        ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "full start game"),
    };

    [Fact]
    public void LongRandomPlayouts_IncrementalMatchesFullRefreshAtEveryPly()
    {
        var promotions = 0;
        var castles = 0;
        var enPassants = 0;
        var bucketCrossings = 0;
        var plies = 0;

        foreach (var (fen, _) in PlayoutCorpus)
        {
            foreach (var seed in new[] { 1, 2, 3 })
            {
                var random = new Random(seed * 7919 + fen.Length);
                var (searcher, gameState, tt) = Create(fen);
                try
                {
                    searcher.Boards[0] = default;
                    gameState.Board.CloneTo(ref searcher.Boards[0]);
                    searcher.FillInitialAccumulators(&searcher.Boards[0], searcher.Accumulators);

                    var previousWhiteBucket = NnueOracle.OracleInputBucket(&searcher.Boards[0], true);

                    for (var ply = 1; ply <= 60 && !gameState.GameOver(); ply++)
                    {
                        var move = gameState.LegalMoves[random.Next(gameState.LegalMoves.Count)];
                        var moveType = move.GetMoveType();
                        promotions += moveType is >= 4 and <= 7 ? 1 : 0;
                        castles += moveType == Constants.Castle ? 1 : 0;
                        enPassants += moveType == Constants.EnPassant ? 1 : 0;

                        var parent = searcher.Boards + ply - 1;
                        var board = searcher.Boards + ply;
                        parent->CloneTo(ref *board);

                        // Search-identical call order: side/EP/castle values come from the parent
                        // before PartialApply; FinishApply flips the side flag.
                        var whiteToMove = parent->WhiteToMove;
                        var ok = whiteToMove ? board->PartialApplyWhite(move) : board->PartialApplyBlack(move);
                        Assert.True(ok, "legal moves must partially apply");
                        board->UpdateCheckStatus();

                        var accumulator = searcher.Accumulators + ply;
                        accumulator->UpdateToParent(searcher.Accumulators + ply - 1, board);
                        if (whiteToMove)
                            board->FinishApplyWhite(ref *accumulator, move, parent->EnPassantFile, Zobrist.GetEnPassantHashFile(ref *parent), parent->CastleRights);
                        else
                            board->FinishApplyBlack(ref *accumulator, move, parent->EnPassantFile, Zobrist.GetEnPassantHashFile(ref *parent), parent->CastleRights);

                        gameState.Apply(move);

                        var whiteBucket = NnueOracle.OracleInputBucket(board, true);
                        bucketCrossings += whiteBucket != previousWhiteBucket ? 1 : 0;
                        previousWhiteBucket = whiteBucket;

                        var eval = searcher.Evaluate(board, accumulator, ply);
                        var expected = NnueOracle.OracleEvalFromScratch(board);
                        Assert.True(eval == expected, $"incremental accumulation must equal full refresh: {fen} seed {seed} ply {ply} move {move.ToUciMoveName()} type {moveType}");
                        plies++;
                    }
                }
                finally
                {
                    searcher.Dispose();
                    gameState.Dispose();
                    Free(tt);
                }
            }
        }

        Assert.True(plies > 300);
        Assert.True(promotions > 0, "corpus must exercise promotions");
        Assert.True(castles > 0, "corpus must exercise castling");
        Assert.True(bucketCrossings > 0, "corpus must exercise king-bucket crossings");
    }

    [Fact]
    public void WhiteEnPassantUpdate_MatchesScalarOracle()
    {
        // e5xd6 e.p.: pawn e5(36)->d6(43), captured black pawn d5(35).
        SinglePlyUpdateMatchesOracle("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", Constants.EnPassant,
            [(Constants.WhitePawn, 36, -1), (Constants.WhitePawn, 43, +1), (Constants.BlackPawn, 35, -1)]);
    }

    [Fact]
    public void BlackEnPassantUpdate_MatchesScalarOracle()
    {
        // d4xe3 e.p.: pawn d4(27)->e3(20), captured white pawn e4(28).
        SinglePlyUpdateMatchesOracle("4k3/8/8/8/3pP3/8/8/4K3 b - e3 0 1", Constants.EnPassant,
            [(Constants.BlackPawn, 27, -1), (Constants.BlackPawn, 20, +1), (Constants.WhitePawn, 28, -1)]);
    }

    [Fact]
    public void PromotionUpdate_MatchesScalarOracle()
    {
        // b8=Q: pawn b7(49) removed, queen b8(57) added.
        SinglePlyUpdateMatchesOracle("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1", Constants.PawnQueenPromotion,
            [(Constants.WhitePawn, 49, -1), (Constants.WhiteQueen, 57, +1)]);
    }

    private static void SinglePlyUpdateMatchesOracle(string fen, byte moveType,
        (byte piece, int square, int sign)[] expectedUpdates)
    {
        var (searcher, gameState, tt) = Create(fen);
        try
        {
            searcher.Boards[0] = default;
            gameState.Board.CloneTo(ref searcher.Boards[0]);
            searcher.FillInitialAccumulators(&searcher.Boards[0], searcher.Accumulators);

            var move = gameState.LegalMoves.First(m => m.GetMoveType() == moveType);

            var parent = searcher.Boards;
            var child = searcher.Boards + 1;
            parent->CloneTo(ref *child);
            var whiteToMove = parent->WhiteToMove;
            Assert.True((whiteToMove ? child->PartialApplyWhite(move) : child->PartialApplyBlack(move)));
            child->UpdateCheckStatus();

            var accumulator = searcher.Accumulators + 1;
            accumulator->UpdateToParent(searcher.Accumulators, child);
            if (whiteToMove)
                child->FinishApplyWhite(ref *accumulator, move, parent->EnPassantFile, Zobrist.GetEnPassantHashFile(ref *parent), parent->CastleRights);
            else
                child->FinishApplyBlack(ref *accumulator, move, parent->EnPassantFile, Zobrist.GetEnPassantHashFile(ref *parent), parent->CastleRights);

            searcher.ApplyWhiteUpdate(1, accumulator);
            searcher.ApplyBlackUpdate(1, accumulator);

            var expectedWhite = NnueOracle.OracleApply(ReadAcc((short*)searcher.WhiteAccumulators[0]), child,
                expectedUpdates.Select(u => (u.piece, u.square, u.sign, true)).ToArray());
            Assert.True(expectedWhite.SequenceEqual(ReadAcc((short*)searcher.WhiteAccumulators[1])), $"white-view update: {fen} {moveType}");

            var expectedBlack = NnueOracle.OracleApply(ReadAcc((short*)searcher.BlackAccumulators[0]), child,
                expectedUpdates.Select(u => (u.piece, u.square, u.sign, false)).ToArray());
            Assert.True(expectedBlack.SequenceEqual(ReadAcc((short*)searcher.BlackAccumulators[1])), $"black-view update: {fen} {moveType}");
        }
        finally
        {
            searcher.Dispose();
            gameState.Dispose();
            Free(tt);
        }
    }

    [Fact]
    public void KernelEquivalence_AdversarialData_MatchesScalarReference()
    {
        var random = new Random(4242);
        short[] Rand() => Enumerable.Range(0, NnueWeights.Layer1Size)
            .Select(_ => (short)random.NextInt64(short.MinValue, short.MaxValue)).ToArray();

        var sourceArr = Rand();
        var sub1Arr = Rand();
        var sub2Arr = Rand();
        var add1Arr = Rand();
        var add2Arr = Rand();
        var destArr = new short[NnueWeights.Layer1Size];

        fixed (short* source = sourceArr, sub1 = sub1Arr, sub2 = sub2Arr, add1 = add1Arr, add2 = add2Arr, dest = destArr)
        {
            Searcher.SimdCopy(dest, source);
            Assert.True(Read(source).SequenceEqual(Read(dest)), "SimdCopy");

            Searcher.Sub(source, dest, sub1);
            Assert.True(ExpectSub(source, sub1, null).SequenceEqual(Read(dest)), "Sub");

            Searcher.Add(source, dest, add1);
            Assert.True(ExpectAdd(source, add1).SequenceEqual(Read(dest)), "Add");

            Searcher.SubAdd(source, dest, sub1, add1);
            Assert.True(ExpectSubAdd(source, sub1, add1).SequenceEqual(Read(dest)), "SubAdd");

            Searcher.SubSubAdd(source, dest, sub1, sub2, add1);
            Assert.True(ExpectSubSubAdd(source, sub1, sub2, add1).SequenceEqual(Read(dest)), "SubSubAdd");

            Searcher.SubSubAddAdd(source, dest, sub1, sub2, add1, add2);
            Assert.True(ExpectSubSubAddAdd(source, sub1, sub2, add1, add2).SequenceEqual(Read(dest)), "SubSubAddAdd");

            // Aliasing contract: feature pointers may alias each other (element-wise ops);
            // sub == add must be an identity on the source.
            Searcher.SubAdd(source, dest, sub1, sub1);
            Assert.True(Read(source).SequenceEqual(Read(dest)), "SubAdd with aliased feature pointers is identity");
            Searcher.SubSubAddAdd(source, dest, sub1, sub2, sub2, sub1);
            Assert.True(Read(source).SequenceEqual(Read(dest)), "SubSubAddAdd with fully crossed feature aliasing is identity");

            // AddWeights is documented in-place (accumulator += feature).
            Searcher.AddWeights(dest, add1);
            Assert.True(ExpectAdd(source, add1).SequenceEqual(Read(dest)), "AddWeights in-place");
        }
    }

    private static short[] ExpectSub(short* s, short* a, short* b)
    {
        var r = new short[NnueWeights.Layer1Size];
        for (var i = 0; i < r.Length; i++) r[i] = unchecked((short)(unchecked((short)(s[i] - a[i])) - (b == null ? (short)0 : b[i])));
        return r;
    }

    private static short[] ExpectAdd(short* s, short* a)
    {
        var r = new short[NnueWeights.Layer1Size];
        for (var i = 0; i < r.Length; i++) r[i] = unchecked((short)(s[i] + a[i]));
        return r;
    }

    private static short[] ExpectSubAdd(short* s, short* sub, short* add)
    {
        var r = new short[NnueWeights.Layer1Size];
        for (var i = 0; i < r.Length; i++) r[i] = unchecked((short)(unchecked((short)(s[i] - sub[i])) + add[i]));
        return r;
    }

    private static short[] ExpectSubSubAdd(short* s, short* sub1, short* sub2, short* add)
    {
        var r = new short[NnueWeights.Layer1Size];
        for (var i = 0; i < r.Length; i++) r[i] = unchecked((short)(unchecked((short)(unchecked((short)(s[i] - sub1[i])) - sub2[i])) + add[i]));
        return r;
    }

    private static short[] ExpectSubSubAddAdd(short* s, short* sub1, short* sub2, short* add1, short* add2)
    {
        var r = new short[NnueWeights.Layer1Size];
        for (var i = 0; i < r.Length; i++) r[i] = unchecked((short)(unchecked((short)(unchecked((short)(unchecked((short)(s[i] - sub1[i])) - sub2[i])) + add1[i])) + add2[i]));
        return r;
    }

    [Fact]
    public void CastleUpdate_MatchesScalarOracle()
    {
        // White O-O from the classic castling fixture: SubSubAddAdd (king+rook pairs).
        var (searcher, gameState, tt) = Create("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        var board = gameState.Board;
        try
        {
            searcher.Boards[0] = default;
            board.CloneTo(ref searcher.Boards[0]);
            searcher.FillInitialAccumulators(&searcher.Boards[0], searcher.Accumulators);

            var move = gameState.LegalMoves.First(m => m.GetMoveType() == Constants.Castle && m.GetFromSquare() == 4);
            var parent = searcher.Boards;
            var child = searcher.Boards + 1;
            parent->CloneTo(ref *child);
            Assert.True(child->PartialApplyWhite(move));
            child->UpdateCheckStatus();

            var accumulator = searcher.Accumulators + 1;
            accumulator->UpdateToParent(searcher.Accumulators, child);
            child->FinishApplyWhite(ref *accumulator, move, parent->EnPassantFile, Zobrist.GetEnPassantHashFile(ref *parent), parent->CastleRights);

            Assert.True(accumulator->ChangeType == AccumulatorChangeType.SubSubAddAdd, "castling must use the four-feature update");

            searcher.ApplyWhiteUpdate(1, accumulator);
            searcher.ApplyBlackUpdate(1, accumulator);

            // a1=0 rook, e1=4 king, o-o: king e1->g1(6), rook h1(7)->f1(5).
            var expectedWhite = NnueOracle.OracleApply(ReadAcc((short*)searcher.WhiteAccumulators[0]), child,
                (Constants.WhiteKing, 4, -1, true), (Constants.WhiteRook, 7, -1, true),
                (Constants.WhiteKing, 6, +1, true), (Constants.WhiteRook, 5, +1, true));
            Assert.True(expectedWhite.SequenceEqual(ReadAcc((short*)searcher.WhiteAccumulators[1])), "white castle SubSubAddAdd");

            var expectedBlack = NnueOracle.OracleApply(ReadAcc((short*)searcher.BlackAccumulators[0]), child,
                (Constants.WhiteKing, 4, -1, false), (Constants.WhiteRook, 7, -1, false),
                (Constants.WhiteKing, 6, +1, false), (Constants.WhiteRook, 5, +1, false));
            Assert.True(expectedBlack.SequenceEqual(ReadAcc((short*)searcher.BlackAccumulators[1])), "black-view castle SubSubAddAdd");
        }
        finally
        {
            searcher.Dispose();
            gameState.Dispose();
            Free(tt);
        }
    }

    private static (Searcher searcher, GameState gameState, nint tt) Create(string fen)
    {
        var ttCount = (int)TranspositionTableExtensions.CalculateTranspositionTableSize(2);
        var tt = Bench.AllocateTranspositions((nuint)ttCount);
        var gameState = new GameState(BoardStateExtensions.CreateBoardFromFen(fen));
        var searcher = new Searcher(tt, ttCount);
        return (searcher, gameState, (nint)tt);
    }

    private static unsafe void Free(nint tt) => MemoryHelpers.FreeAligned((void*)tt);

    private static short[] Read(short* p) =>
        Enumerable.Range(0, NnueWeights.Layer1Size).Select(i => p[i]).ToArray();

    private static short[] ReadAcc(short* p) => Read(p);
}
