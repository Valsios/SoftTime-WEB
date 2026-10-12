using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class PeriodGroupingTests
{
    [Fact]
    public void Quotidienne_LabelIsIsoDate()
    {
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 7, 1, 8, 30, 0), PeriodGrouping.Quotidienne);
        Assert.Equal(new DateTime(2025, 7, 1), cle);
        Assert.Equal("2025-07-01", libelle);
    }

    [Fact]
    public void Mensuelle_LabelIsMonthYear()
    {
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 7, 1), PeriodGrouping.Mensuelle);
        Assert.Equal(new DateTime(2025, 7, 1), cle);
        Assert.Equal("07-2025", libelle);
    }

    [Fact]
    public void Hebdomadaire_LabelIsReadableWeekRange()
    {
        // Le 1er juillet 2025 (mardi) appartient à la semaine ISO 27 : lundi 30/06 → dimanche 06/07.
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 7, 1), PeriodGrouping.Hebdomadaire);
        Assert.Equal(new DateTime(2025, 6, 30), cle);
        Assert.Equal("Semaine 27 (30/06 - 06/07/2025)", libelle);
    }

    [Fact]
    public void Hebdomadaire_AllDaysOfWeekShareTheSameGroup()
    {
        var lundi = new DateTime(2025, 6, 30);
        var premier = PeriodGrouping.Grouper(lundi, PeriodGrouping.Hebdomadaire);
        for (var i = 1; i <= 6; i++)
            Assert.Equal(premier, PeriodGrouping.Grouper(lundi.AddDays(i), PeriodGrouping.Hebdomadaire));
    }

    [Fact]
    public void Hebdomadaire_WeekNumberUsesIsoYear_NotCalendarYear()
    {
        // Le 29/12/2025 (lundi) ouvre la semaine ISO 1 de 2026, qui déborde sur 2025.
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 12, 30), PeriodGrouping.Hebdomadaire);
        Assert.Equal(new DateTime(2025, 12, 29), cle);
        Assert.Equal("Semaine 1 (29/12 - 04/01/2026)", libelle);
    }

    [Fact]
    public void Hebdomadaire_KeysSortChronologicallyAcrossYearBoundary()
    {
        var decembre = PeriodGrouping.Grouper(new DateTime(2025, 12, 30), PeriodGrouping.Hebdomadaire);
        var janvier = PeriodGrouping.Grouper(new DateTime(2026, 1, 6), PeriodGrouping.Hebdomadaire);
        Assert.True(decembre.Cle < janvier.Cle);
    }

    [Fact]
    public void UnknownPeriode_FallsBackToMonthly()
    {
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 7, 15), "inconnue");
        Assert.Equal(new DateTime(2025, 7, 1), cle);
        Assert.Equal("07-2025", libelle);
    }

    [Fact]
    public void Clefs_Weekly_CoversEveryWeekWithoutGap()
    {
        var cles = PeriodGrouping.Clefs(
            new DateTime(2025, 6, 30), new DateTime(2025, 7, 20), PeriodGrouping.Hebdomadaire);

        Assert.Equal(3, cles.Count);
        Assert.Equal(new DateTime(2025, 6, 30), cles[0].Cle);
        Assert.Equal(new DateTime(2025, 7, 7), cles[1].Cle);
        Assert.Equal(new DateTime(2025, 7, 14), cles[2].Cle);
    }

    [Fact]
    public void Clefs_Weekly_StaysChronologicalAcrossYearBoundary()
    {
        var cles = PeriodGrouping.Clefs(
            new DateTime(2025, 12, 29), new DateTime(2026, 1, 6), PeriodGrouping.Hebdomadaire);

        Assert.Equal(2, cles.Count);
        Assert.Equal("Semaine 1 (29/12 - 04/01/2026)", cles[0].Libelle);
        Assert.Equal("Semaine 2 (05/01 - 11/01/2026)", cles[1].Libelle);
        Assert.True(cles[0].Cle < cles[1].Cle);
    }

    [Fact]
    public void Clefs_Monthly_IncludesPartialFirstMonth()
    {
        var cles = PeriodGrouping.Clefs(
            new DateTime(2025, 7, 15), new DateTime(2025, 8, 2), PeriodGrouping.Mensuelle);

        Assert.Equal(2, cles.Count);
        Assert.Equal("07-2025", cles[0].Libelle);
        Assert.Equal("08-2025", cles[1].Libelle);
    }

    [Fact]
    public void Clefs_Daily_ReturnsOneKeyPerDay()
    {
        var cles = PeriodGrouping.Clefs(
            new DateTime(2025, 7, 1), new DateTime(2025, 7, 3), PeriodGrouping.Quotidienne);

        Assert.Equal(3, cles.Count);
        Assert.Equal("2025-07-01", cles[0].Libelle);
    }

    [Fact]
    public void Clefs_ReversedRange_IsEmpty()
    {
        Assert.Empty(PeriodGrouping.Clefs(
            new DateTime(2025, 7, 10), new DateTime(2025, 7, 1), PeriodGrouping.Quotidienne));
    }
}
