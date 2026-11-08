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
        var result = PunchPairingRules.PairSimpleDay(Array.Empty<PunchMoment>());
        result.Pairs.Should().BeEmpty();
        result.Unpaired.Should().BeEmpty();
    }

    [Fact]
    public void PairSimpleDay_SinglePunch_IsUnpaired()
    {
        var result = PunchSimple(M(Lundi, 8));
        result.Pairs.Should().BeEmpty();
        result.Unpaired.Should().ContainSingle();
    }

    [Fact]
    public void PairSimpleDay_TwoPunches_FirstIsEntryLastIsExit()
    {
        var result = PunchSimple(M(Lundi, 8), M(Lundi, 17));
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(17, 0, 0));
        result.Unpaired.Should().BeEmpty();
    }

    [Fact]
    public void PairSimpleDay_FourPunches_KeepsOnlyFirstAndLast()
    {
        var result = PunchSimple(M(Lundi, 8), M(Lundi, 12), M(Lundi, 13), M(Lundi, 17));
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(17, 0, 0));
    }

    [Fact]
    public void PairSimpleDay_UnsortedInput_IsOrdered()
    {
        var result = PunchSimple(M(Lundi, 17), M(Lundi, 8));
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(17, 0, 0));
    }

    // ---- Mode multi-pointage (seuil de 15 minutes) ----

    [Fact]
    public void PairMultipleDay_TwoPunchesAboveGap_FormOnePair()
    {
        var result = PunchMultiple(M(Lundi, 8, 0), M(Lundi, 8, 16));
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(8, 16, 0));
    }

    [Fact]
    public void PairMultipleDay_TwoPunchesWithinGap_AreUnpaired()
    {
        // Comportement hérité du desktop : deux pointages à moins de 15 minutes ne forment pas une paire.
        var result = PunchMultiple(M(Lundi, 8, 0), M(Lundi, 8, 10));
        result.Pairs.Should().BeEmpty();
        result.Unpaired.Should().ContainSingle();
    }

    [Fact]
    public void PairMultipleDay_OddCount_IsUnpaired()
    {
        var result = PunchMultiple(M(Lundi, 8), M(Lundi, 13), M(Lundi, 17));
        result.Pairs.Should().BeEmpty();
        result.Unpaired.Count.Should().Be(3);
    }

    [Fact]
    public void PairMultipleDay_LunchBreak_FormsTwoPairs()
    {
        var result = PunchMultiple(M(Lundi, 8), M(Lundi, 12), M(Lundi, 13), M(Lundi, 17));
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
        var result = PunchMultiple(M(Lundi, 22), M(Lundi.AddDays(1), 6));
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeDate.Date.Should().Be(Lundi.Date);
        pair.SortieDate.Date.Should().Be(Lundi.AddDays(1).Date);
        pair.EntreeHeure.Should().Be(new TimeSpan(22, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(6, 0, 0));
    }

    [Fact]
    public void PairMultipleDay_CustomGap_IsHonoured()
    {
        var points = new[] { M(Lundi, 8, 0), M(Lundi, 8, 10) };
        var result = PunchPairingRules.PairMultipleDay(points, TimeSpan.FromMinutes(5));
        var pair = result.Pairs.Should().ContainSingle().Which;
        pair.EntreeHeure.Should().Be(new TimeSpan(8, 0, 0));
        pair.SortieHeure.Should().Be(new TimeSpan(8, 10, 0));
    }

    [Fact]
    public void MultiPunchGap_IsFifteenMinutes()
    {
        PunchPairingRules.MultiPunchGap.Should().Be(new TimeSpan(0, 15, 0));
    }

    private static PunchPairingResult PunchSimple(params PunchMoment[] points) =>
        PunchPairingRules.PairSimpleDay(points);

    private static PunchPairingResult PunchMultiple(params PunchMoment[] points) =>
        PunchPairingRules.PairMultipleDay(points);
}
