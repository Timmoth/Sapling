using FluentAssertions;

namespace Sapling.Engine.Tests;

/// <summary>
/// The validator must enforce the unsafe-engine invariants (exactly one king per side, a legal
/// piece census), not just rank syntax.
/// </summary>
public class FenValidatorTests
{
    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/8 w - - 0 1", "no white king")]
    [InlineData("8/8/8/8/8/8/8/4K3 w - - 0 1", "no black king")]
    [InlineData("4k3/8/8/8/8/8/8/4K1K1 w - - 0 1", "two white kings")]
    [InlineData("4k3/PPPPPPPPP/8/8/8/8/8/4K3 w - - 0 1", "nine white pawns")]
    [InlineData("4k3/8/8/8/8/8/PPPPPPPP/PPPPK3 w - - 0 1", "too many white pieces")]
    public void Rejects_illegal_census(string fen, string because)
    {
        FenValidator.Validate(fen).Should().NotBeNull(because);
    }

    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1")]
    [InlineData("4k3/8/8/8/8/8/QQQQQQQQ/Q3K3 w - - 0 1")] // nine queens + king: promoted, census legal
    public void Accepts_legal_census(string fen)
    {
        FenValidator.Validate(fen).Should().BeNull();
    }
}
