using FluentAssertions;
using Sapling.Engine.Transpositions;

namespace Sapling.Engine.Tests;

/// <summary>
/// Independent expected-value tests for the TT move-identity rule. A zero-move store may
/// keep the incumbent move only for the SAME position; across a hash collision the incumbent move
/// belongs to a different position and must be cleared. The randomized reference test uses only
/// nonzero moves, so it cannot catch cross-position move leakage.
/// </summary>
public class TranspositionMoveIdentityTests
{
    private const ulong HashA = 0x1111_2222_3333_4444UL;
    private const ulong HashB = 0xAAAA_BBBB_CCCC_DDDDUL;

    [Fact]
    public void ZeroMoveStoreAcrossCollision_ClearsMove()
    {
        var entry = default(Transposition);
        TranspositionTableExtensions.Set(ref entry, HashA, 8, 0, 10, TranspositionTableFlag.Exact, 1234, shared: false);
        entry.Move.Should().Be(1234);

        // Different position, move-less store: must not keep A's move.
        TranspositionTableExtensions.Set(ref entry, HashB, 8, 0, 20, TranspositionTableFlag.Exact, 0, shared: false);
        entry.FullHash.Should().Be(HashB);
        entry.Move.Should().Be(0u);
    }

    [Fact]
    public void ZeroMoveStoreSamePosition_PreservesMove()
    {
        var entry = default(Transposition);
        TranspositionTableExtensions.Set(ref entry, HashA, 4, 0, 10, TranspositionTableFlag.Exact, 1234, shared: false);

        // Same position, move-less store: keep the incumbent move.
        TranspositionTableExtensions.Set(ref entry, HashA, 8, 0, 20, TranspositionTableFlag.Exact, 0, shared: false);
        entry.FullHash.Should().Be(HashA);
        entry.Move.Should().Be(1234u);
    }

    [Fact]
    public void NonZeroMoveStoreAcrossCollision_InstallsNewMove()
    {
        var entry = default(Transposition);
        TranspositionTableExtensions.Set(ref entry, HashA, 8, 0, 10, TranspositionTableFlag.Exact, 1234, shared: false);
        TranspositionTableExtensions.Set(ref entry, HashB, 8, 0, 20, TranspositionTableFlag.Exact, 5678, shared: false);
        entry.Move.Should().Be(5678u);
    }

    [Fact]
    public void LowerDepthNonExactStore_IsRejected()
    {
        var entry = default(Transposition);
        TranspositionTableExtensions.Set(ref entry, HashA, 8, 0, 10, TranspositionTableFlag.Exact, 1234, shared: false);

        // Same position, shallower, non-exact: replacement policy rejects, entry untouched.
        TranspositionTableExtensions.Set(ref entry, HashA, 4, 0, 20, TranspositionTableFlag.Alpha, 0, shared: false);
        entry.Depth.Should().Be(8);
        entry.Flag.Should().Be(TranspositionTableFlag.Exact);
        entry.Move.Should().Be(1234u);
    }

    [Fact]
    public void ExactStoreAtLowerDepth_StillReplaces_AndKeepsSamePositionMove()
    {
        var entry = default(Transposition);
        TranspositionTableExtensions.Set(ref entry, HashA, 8, 0, 10, TranspositionTableFlag.Beta, 1234, shared: false);

        // PV/exact data always replaces; a move-less exact store keeps the same-position incumbent.
        TranspositionTableExtensions.Set(ref entry, HashA, 4, 0, 20, TranspositionTableFlag.Exact, 0, shared: false);
        entry.Depth.Should().Be(4);
        entry.Flag.Should().Be(TranspositionTableFlag.Exact);
        entry.Move.Should().Be(1234u);
    }

    [Fact]
    public void SharedPath_ZeroMoveSamePosition_PreservesMove_AndPublishesEvenVersion()
    {
        var entry = default(Transposition);
        TranspositionTableExtensions.Set(ref entry, HashA, 4, 0, 10, TranspositionTableFlag.Exact, 1234, shared: true);
        TranspositionTableExtensions.Set(ref entry, HashA, 8, 0, 20, TranspositionTableFlag.Exact, 0, shared: true);

        entry.Move.Should().Be(1234u);
        entry.Depth.Should().Be(8);
        (entry.Version & 1).Should().Be(0, "the shared store must publish a stable even version");
    }
}
