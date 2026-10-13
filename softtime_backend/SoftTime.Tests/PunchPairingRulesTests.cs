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
        Assert.Empty(result.Pairs);
        Assert.Empty(result.Unpaired);
    }

    [Fact]
    public void PairSimpleDay_SinglePunch_IsUnpaired()
    {
        var result = PunchSimple(M(Lundi, 8));
        Assert.Empty(result.Pairs);
        Assert.Single(result.Unpaired);
    }

    [Fact]
    public void PairSimpleDay_TwoPunches_FirstIsEntryLastIsExit()
    {
        var result = PunchSimple(M(Lundi, 8), M(Lundi, 17));
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), pair.EntreeHeure);
        Assert.Equal(new TimeSpan(17, 0, 0), pair.SortieHeure);
        Assert.Empty(result.Unpaired);
    }

    [Fact]
    public void PairSimpleDay_FourPunches_KeepsOnlyFirstAndLast()
    {
        var result = PunchSimple(M(Lundi, 8), M(Lundi, 12), M(Lundi, 13), M(Lundi, 17));
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), pair.EntreeHeure);
        Assert.Equal(new TimeSpan(17, 0, 0), pair.SortieHeure);
    }

    [Fact]
    public void PairSimpleDay_UnsortedInput_IsOrdered()
    {
        var result = PunchSimple(M(Lundi, 17), M(Lundi, 8));
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), pair.EntreeHeure);
        Assert.Equal(new TimeSpan(17, 0, 0), pair.SortieHeure);
    }

    // ---- Mode multi-pointage (seuil de 15 minutes) ----

    [Fact]
    public void PairMultipleDay_TwoPunchesAboveGap_FormOnePair()
    {
        var result = PunchMultiple(M(Lundi, 8, 0), M(Lundi, 8, 16));
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), pair.EntreeHeure);
        Assert.Equal(new TimeSpan(8, 16, 0), pair.SortieHeure);
    }

    [Fact]
    public void PairMultipleDay_TwoPunchesWithinGap_AreUnpaired()
    {
        // Comportement hérité du desktop : deux pointages à moins de 15 minutes ne forment pas une paire.
        var result = PunchMultiple(M(Lundi, 8, 0), M(Lundi, 8, 10));
        Assert.Empty(result.Pairs);
        Assert.Single(result.Unpaired);
    }

    [Fact]
    public void PairMultipleDay_OddCount_IsUnpaired()
    {
        var result = PunchMultiple(M(Lundi, 8), M(Lundi, 13), M(Lundi, 17));
        Assert.Empty(result.Pairs);
        Assert.Equal(3, result.Unpaired.Count);
    }

    [Fact]
    public void PairMultipleDay_LunchBreak_FormsTwoPairs()
    {
        var result = PunchMultiple(M(Lundi, 8), M(Lundi, 12), M(Lundi, 13), M(Lundi, 17));
        Assert.Equal(2, result.Pairs.Count);
        Assert.Equal(new TimeSpan(8, 0, 0), result.Pairs[0].EntreeHeure);
        Assert.Equal(new TimeSpan(12, 0, 0), result.Pairs[0].SortieHeure);
        Assert.Equal(new TimeSpan(13, 0, 0), result.Pairs[1].EntreeHeure);
        Assert.Equal(new TimeSpan(17, 0, 0), result.Pairs[1].SortieHeure);
        Assert.Empty(result.Unpaired);
    }

    [Fact]
    public void PairMultipleDay_NightShift_SpansTwoDays()
    {
        var result = PunchMultiple(M(Lundi, 22), M(Lundi.AddDays(1), 6));
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(Lundi.Date, pair.EntreeDate.Date);
        Assert.Equal(Lundi.AddDays(1).Date, pair.SortieDate.Date);
        Assert.Equal(new TimeSpan(22, 0, 0), pair.EntreeHeure);
        Assert.Equal(new TimeSpan(6, 0, 0), pair.SortieHeure);
    }

    [Fact]
    public void PairMultipleDay_CustomGap_IsHonoured()
    {
        var points = new[] { M(Lundi, 8, 0), M(Lundi, 8, 10) };
        var result = PunchPairingRules.PairMultipleDay(points, TimeSpan.FromMinutes(5));
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), pair.EntreeHeure);
        Assert.Equal(new TimeSpan(8, 10, 0), pair.SortieHeure);
    }

    [Fact]
    public void MultiPunchGap_IsFifteenMinutes()
    {
        Assert.Equal(new TimeSpan(0, 15, 0), PunchPairingRules.MultiPunchGap);
    }

    private static PunchPairingResult PunchSimple(params PunchMoment[] points) =>
        PunchPairingRules.PairSimpleDay(points);

    private static PunchPairingResult PunchMultiple(params PunchMoment[] points) =>
        PunchPairingRules.PairMultipleDay(points);
}
