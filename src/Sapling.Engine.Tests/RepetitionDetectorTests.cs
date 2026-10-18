
namespace Sapling.Engine.Tests;

/// <summary>
/// Direct unit coverage of the threefold-repetition walk over the hash history. Repetition draws
/// are decided entirely by IsThreefoldRepetition; a refactor that shifts the offset/step/counting
/// would mis-score lines, so the exact counting semantics are pinned here.
/// </summary>
public unsafe class RepetitionDetectorTests
{
    private const ulong Current = 0x1111_2222_3333_4444UL;

    private static bool Threefold(ushort turnCount, ushort halfMoveClock, ulong[] history)
    {
        fixed (ulong* h = history)
            return RepetitionDetector.IsThreefoldRepetition(turnCount, halfMoveClock, h);
    }

    [Fact]
    public void ThreeOccurrencesAtEvenOffsets_IsThreefold()
    {
        // Current hash at index 5; two earlier matches at indices 3 and 1 (even offsets).
        var history = new ulong[] { 0xA1, Current, 0xA2, Current, 0xA3, Current };
        Assert.True(Threefold(turnCount: 6, halfMoveClock: 6, history));
    }

    [Fact]
    public void TwoOccurrences_IsNotThreefold()
    {
        var history = new ulong[] { 0xA1, 0xA9, 0xA2, Current, 0xA3, Current };
        Assert.False(Threefold(turnCount: 6, halfMoveClock: 6, history));
    }

    [Fact]
    public void BelowHalfMoveClockThreshold_IsNeverThreefold()
    {
        var history = new ulong[] { Current, Current, Current, Current, Current, Current };
        Assert.False(Threefold(turnCount: 6, halfMoveClock: 2, history));
    }

    [Fact]
    public void MatchesBeyondTheHalfMoveClockWindow_AreIgnored()
    {
        // The two earlier matches sit at offsets 6 and 8, beyond a window of 4 plies.
        var history = new ulong[] { Current, 0xB1, Current, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, Current };
        Assert.False(Threefold(turnCount: 9, halfMoveClock: 4, history));
    }

    [Fact]
    public void MatchAtHalfMoveClockBoundary_IsIncluded()
    {
        // The oldest position in the reversible window is exactly halfMoveClock
        // plies behind the current one and still counts as an occurrence.
        var history = new ulong[] { 0xA1, 0xA2, 0xA3, 0xA3, Current, 0xA5, 0xA6, 0xA7, Current };
        Assert.False(Threefold(turnCount: 9, halfMoveClock: 8, history));

        history[0] = Current;
        Assert.True(Threefold(turnCount: 9, halfMoveClock: 8, history));
    }

    [Fact]
    public void ZeroTurnCount_IsNeverThreefold()
    {
        var history = new ulong[] { Current, Current, Current };
        Assert.False(Threefold(turnCount: 0, halfMoveClock: 6, history));
    }
}
