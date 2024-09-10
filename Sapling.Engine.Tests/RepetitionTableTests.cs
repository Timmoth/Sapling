using FluentAssertions;

namespace Sapling.Engine.Tests;

public class RepetitionTableTests
{
    [Fact]
    public void Detects_ThreeFoldRepetition_FromHistory()
    {
        // Given
        var repetitionTable = new RepetitionTable();
        var history = new Stack<ulong>();
        history.Push(1);
        history.Push(1);
        repetitionTable.Init(history);

        // When
        var repetitionDetected = repetitionTable.DetectThreeFoldRepetition(1);

        // Then
        repetitionDetected.Should().Be(true);
    }

    [Fact]
    public void Detects_ThreeFoldRepetition_FromPush()
    {
        // Given
        var repetitionTable = new RepetitionTable();
        var history = new Stack<ulong>();
        repetitionTable.Init(history);

        repetitionTable.Push(1, false);
        repetitionTable.Push(1, false);

        // When
        var repetitionDetected = repetitionTable.DetectThreeFoldRepetition(1);

        // Then
        repetitionDetected.Should().Be(true);
    }

    [Fact]
    public void Detects_ThreeFoldRepetition_FromHybrid()
    {
        // Given
        var repetitionTable = new RepetitionTable();
        var history = new Stack<ulong>();
        history.Push(1);
        repetitionTable.Init(history);

        repetitionTable.Push(1, false);

        // When
        var repetitionDetected = repetitionTable.DetectThreeFoldRepetition(1);

        // Then
        repetitionDetected.Should().Be(true);
    }

    [Fact]
    public void DoesNotDetect_ThreeFoldRepetition_OnEmpty()
    {
        // Given
        var repetitionTable = new RepetitionTable();
        var history = new Stack<ulong>();
        repetitionTable.Init(history);

        // When
        var repetitionDetected = repetitionTable.DetectThreeFoldRepetition(1);

        // Then
        repetitionDetected.Should().Be(false);
    }

    [Fact]
    public void DoesNotDetect_ThreeFoldRepetition_OnNewMove()
    {
        // Given
        var repetitionTable = new RepetitionTable();
        var history = new Stack<ulong>();
        history.Push(1);
        repetitionTable.Init(history);

        repetitionTable.Push(1, false);
        repetitionTable.Push(1, false);

        // When
        var repetitionDetected = repetitionTable.DetectThreeFoldRepetition(2);

        // Then
        repetitionDetected.Should().Be(false);
    }


    [Fact]
    public void DoesNotDetect_ThreeFoldRepetition_AfterReset()
    {
        // Given
        var repetitionTable = new RepetitionTable();
        var history = new Stack<ulong>();
        repetitionTable.Init(history);
        repetitionTable.Push(1, false);
        repetitionTable.Push(1, false);
        repetitionTable.Push(1, true);

        // When
        var repetitionDetected = repetitionTable.DetectThreeFoldRepetition(1);

        // Then
        repetitionDetected.Should().Be(false);
    }
}