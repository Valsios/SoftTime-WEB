using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class PeriodGroupingTests
{
    [Fact]
    public void Grouper_Quotidienne_LabelIsIsoDate()
    {
        // Act
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 7, 1, 8, 30, 0), PeriodGrouping.Quotidienne);

        // Assert
        cle.Should().Be(new DateTime(2025, 7, 1));
        libelle.Should().Be("2025-07-01");
    }

    [Fact]
    public void Grouper_Mensuelle_LabelIsMonthYear()
    {
        // Act
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 7, 1), PeriodGrouping.Mensuelle);

        // Assert
        cle.Should().Be(new DateTime(2025, 7, 1));
        libelle.Should().Be("07-2025");
    }

    [Fact]
    public void Grouper_Hebdomadaire_LabelIsReadableWeekRange()
    {
        // Act
        // Le 1er juillet 2025 (mardi) appartient à la semaine ISO 27 : lundi 30/06 → dimanche 06/07.
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 7, 1), PeriodGrouping.Hebdomadaire);

        // Assert
        cle.Should().Be(new DateTime(2025, 6, 30));
        libelle.Should().Be("Semaine 27 (30/06 - 06/07/2025)");
    }

    [Fact]
    public void Grouper_HebdomadaireDaysOfOneWeek_ShareTheSameGroup()
    {
        // Arrange
        var lundi = new DateTime(2025, 6, 30);

        // Act
        var premier = PeriodGrouping.Grouper(lundi, PeriodGrouping.Hebdomadaire);

        // Assert
        for (var i = 1; i <= 6; i++)
            PeriodGrouping.Grouper(lundi.AddDays(i), PeriodGrouping.Hebdomadaire).Should().Be(premier);
    }

    [Fact]
    public void Hebdomadaire_WeekNumberUsesIsoYear_NotCalendarYear()
    {
        // Act
        // Le 29/12/2025 (lundi) ouvre la semaine ISO 1 de 2026, qui déborde sur 2025.
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 12, 30), PeriodGrouping.Hebdomadaire);

        // Assert
        cle.Should().Be(new DateTime(2025, 12, 29));
        libelle.Should().Be("Semaine 1 (29/12 - 04/01/2026)");
    }

    [Fact]
    public void Grouper_HebdomadaireAcrossYearBoundary_KeysSortChronologically()
    {
        // Arrange
        var decembre = PeriodGrouping.Grouper(new DateTime(2025, 12, 30), PeriodGrouping.Hebdomadaire);

        // Act
        var janvier = PeriodGrouping.Grouper(new DateTime(2026, 1, 6), PeriodGrouping.Hebdomadaire);

        // Assert
        (decembre.Cle < janvier.Cle).Should().BeTrue();
    }

    [Fact]
    public void Grouper_UnknownPeriode_FallsBackToMonthly()
    {
        // Act
        var (cle, libelle) = PeriodGrouping.Grouper(new DateTime(2025, 7, 15), "inconnue");

        // Assert
        cle.Should().Be(new DateTime(2025, 7, 1));
        libelle.Should().Be("07-2025");
    }

    [Fact]
    public void Clefs_Weekly_CoversEveryWeekWithoutGap()
    {
        // Act
        var cles = PeriodGrouping.Clefs(
            new DateTime(2025, 6, 30), new DateTime(2025, 7, 20), PeriodGrouping.Hebdomadaire);

        // Assert
        cles.Count.Should().Be(3);
        cles[0].Cle.Should().Be(new DateTime(2025, 6, 30));
        cles[1].Cle.Should().Be(new DateTime(2025, 7, 7));
        cles[2].Cle.Should().Be(new DateTime(2025, 7, 14));
    }

    [Fact]
    public void Clefs_Weekly_StaysChronologicalAcrossYearBoundary()
    {
        // Act
        var cles = PeriodGrouping.Clefs(
            new DateTime(2025, 12, 29), new DateTime(2026, 1, 6), PeriodGrouping.Hebdomadaire);

        // Assert
        cles.Count.Should().Be(2);
        cles[0].Libelle.Should().Be("Semaine 1 (29/12 - 04/01/2026)");
        cles[1].Libelle.Should().Be("Semaine 2 (05/01 - 11/01/2026)");
        (cles[0].Cle < cles[1].Cle).Should().BeTrue();
    }

    [Fact]
    public void Clefs_Monthly_IncludesPartialFirstMonth()
    {
        // Act
        var cles = PeriodGrouping.Clefs(
            new DateTime(2025, 7, 15), new DateTime(2025, 8, 2), PeriodGrouping.Mensuelle);

        // Assert
        cles.Count.Should().Be(2);
        cles[0].Libelle.Should().Be("07-2025");
        cles[1].Libelle.Should().Be("08-2025");
    }

    [Fact]
    public void Clefs_Daily_ReturnsOneKeyPerDay()
    {
        // Act
        var cles = PeriodGrouping.Clefs(
            new DateTime(2025, 7, 1), new DateTime(2025, 7, 3), PeriodGrouping.Quotidienne);

        // Assert
        cles.Count.Should().Be(3);
        cles[0].Libelle.Should().Be("2025-07-01");
    }

    [Fact]
    public void Clefs_ReversedRange_IsEmpty()
    {
        // Act
        var result = PeriodGrouping.Clefs(
            new DateTime(2025, 7, 10), new DateTime(2025, 7, 1), PeriodGrouping.Quotidienne);

        // Assert
        result.Should().BeEmpty();
    }
}
