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
}
