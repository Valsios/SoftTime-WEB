using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class PunchPairingRulesTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private static PunchMoment M(DateTime date, int h, int m = 0) => new(date, new TimeSpan(h, m, 0));

    // ---- Mode simple (MULTIPOINT = false) ----

    [Fact]
    public void PairSimpleDay_NoPunch_YieldsNothing()
    {
        // Act
        var result = PunchPairingRules.PairSimpleDay(Array.Empty<PunchMoment>());

        // Assert
        result.Pairs.Should().BeEmpty();
        result.Unpaired.Should().BeEmpty();
    }

    [Fact]
    public void PairSimpleDay_SinglePunch_IsUnpaired()
    {
        // Act
        var result = PunchSimple(M(Lundi, 8));

        // Assert
        result.Pairs.Should().BeEmpty();
        result.Unpaired.Should().ContainSingle();
    }

    [Fact]
    public void PairSimpleDay_TwoPunches_FirstIsEntryLastIsExit()
    {
        // Act
        var result = PunchSimple(M(Lundi, 8), M(Lundi, 17));

        // Assert
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(17, 0, 0));
        result.Unpaired.Should().BeEmpty();
    }

    [Fact]
    public void PairSimpleDay_FourPunches_KeepsOnlyFirstAndLast()
    {
        // Act
        var result = PunchSimple(M(Lundi, 8), M(Lundi, 12), M(Lundi, 13), M(Lundi, 17));

        // Assert
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(17, 0, 0));
    }

    [Fact]
    public void PairSimpleDay_UnsortedInput_IsOrdered()
    {
        // Act
        var result = PunchSimple(M(Lundi, 17), M(Lundi, 8));

        // Assert
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(17, 0, 0));
    }

    // ---- Mode multi-pointage (seuil de 15 minutes) ----

    [Fact]
    public void PairMultipleDay_TwoPunchesAboveGap_FormOnePair()
    {
        // Act
        var result = PunchMultiple(M(Lundi, 8, 0), M(Lundi, 8, 16));

        // Assert
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(8, 16, 0));
    }

    [Fact]
    public void PairMultipleDay_TwoPunchesWithinGap_AreUnpaired()
    {
        // Act
        // Comportement hérité du desktop : deux pointages à moins de 15 minutes ne forment pas une paire.
        var result = PunchMultiple(M(Lundi, 8, 0), M(Lundi, 8, 10));

        // Assert
        result.Pairs.Should().BeEmpty();
        result.Unpaired.Should().ContainSingle();
    }

    [Fact]
    public void PairMultipleDay_OddCount_IsUnpaired()
    {
        // Act
        var result = PunchMultiple(M(Lundi, 8), M(Lundi, 13), M(Lundi, 17));

        // Assert
        result.Pairs.Should().BeEmpty();
        result.Unpaired.Count.Should().Be(3);
    }

    [Fact]
    public void PairMultipleDay_LunchBreak_FormsTwoPairs()
    {
        // Act
        var result = PunchMultiple(M(Lundi, 8), M(Lundi, 12), M(Lundi, 13), M(Lundi, 17));

        // Assert
        result.Pairs.Count.Should().Be(2);
        result.Pairs[0].EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        result.Pairs[0].SortieHeure.Should().Be(new TimeSpan(12, 0, 0));
        result.Pairs[1].EntreeHeure.Should().Be(new TimeSpan(13, 0, 0));
        result.Pairs[1].SortieHeure.Should().Be(new TimeSpan(17, 0, 0));
        result.Unpaired.Should().BeEmpty();
    }

    [Fact]
    public void PairMultipleDay_NightShift_SpansTwoDays()
    {
        // Act
        var result = PunchMultiple(M(Lundi, 22), M(Lundi.AddDays(1), 6));

        // Assert
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeDate.Date.Should().Be(Lundi.Date);
        pair.SortieDate.Date.Should().Be(Lundi.AddDays(1).Date);
        pair.EntreeHeure.Should().Be(new TimeSpan(22, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(6, 0, 0));
    }

    [Fact]
    public void PairMultipleDay_CustomGap_IsHonoured()
    {
        // Arrange
        var points = new[] { M(Lundi, 8, 0), M(Lundi, 8, 10) };

        // Act
        var result = PunchPairingRules.PairMultipleDay(points, TimeSpan.FromMinutes(5));

        // Assert
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(8, 10, 0));
    }

    [Fact]
    public void MultiPunchGap_Default_IsFifteenMinutes()
    {
        PunchPairingRules.MultiPunchGap.Should().Be(new TimeSpan(0, 15, 0));
    }

    private static PunchPairingResult PunchSimple(params PunchMoment[] points) =>
        PunchPairingRules.PairSimpleDay(points);

    private static PunchPairingResult PunchMultiple(params PunchMoment[] points) =>
        PunchPairingRules.PairMultipleDay(points);
}
