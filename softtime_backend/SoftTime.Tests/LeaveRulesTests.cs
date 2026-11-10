using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Développement des congés SAGE (portage de <c>Calcul_HSService.LoadCongesForMatricule</c>) :
/// demi-journées aux bornes des périodes, dimanche exclu, codes d'événement 0200/0300 ignorés.
/// </summary>
public class LeaveRulesTests
{
    /// <summary>Lundi 31/08/2026 (les tests d'appariement utilisent déjà cette date comme lundi).</summary>
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private static LeavePeriod Periode(DateTime debut, DateTime fin, bool matin = false, bool apresMidi = false, string? code = "0100")
        => new(debut, fin, matin, apresMidi, code);

    // ---- Codes d'événement ----

    [Theory]
    [InlineData("0100")]
    [InlineData("CP")]
    [InlineData(" 0100 ")]
    public void EstCodeConge_RegularCode_ReturnsTrue(string code)
    {
        // Act
        var result = LeaveRules.EstCodeConge(code);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("0200")]
    [InlineData("0300")]
    [InlineData(" 0200 ")]
    [InlineData("")]
    [InlineData(null)]
    public void EstCodeConge_ExcludedOrEmptyCode_ReturnsFalse(string? code)
    {
        // Act
        var result = LeaveRules.EstCodeConge(code);

        // Assert
        result.Should().BeFalse();
    }

    // ---- Période d'une seule journée ----

    [Fact]
    public void Expandre_SingleDayWithoutFlag_CountsFullDay()
    {
        // Act
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi) });

        // Assert
        var jour = jours.Should().ContainSingle().Which;
        jour.Date.Should().Be(Lundi);
        jour.Valeur.Should().Be(1m);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Expandre_SingleDayWithHalfDayFlag_CountsHalfDay(bool matin, bool apresMidi)
    {
        // Act
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi, matin, apresMidi) });

        // Assert
        jours.Should().ContainSingle().Which.Valeur.Should().Be(0.5m);
    }

    // ---- Périodes de plusieurs jours ----

    [Fact]
    public void Expandre_MultiDayWithoutFlag_CountsEveryDay()
    {
        // Act
        // Lundi → mercredi : 3 journées pleines.
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(2)) });

        // Assert
        jours.Count.Should().Be(3);
        jours.Should().AllSatisfy(j => j.Valeur.Should().Be(1m));
    }

    [Fact]
    public void Expandre_PeriodWithSunday_SkipsSundays()
    {
        // Act
        // Lundi → dimanche inclus : le dimanche est exclu, il reste 6 jours.
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(6)) });

        // Assert
        jours.Count.Should().Be(6);
        jours.Should().NotContain(j => j.Date.DayOfWeek == DayOfWeek.Sunday);
    }

    [Fact]
    public void Expandre_ApresMidiPeriod_StartsWithHalfDay()
    {
        // Act
        // Le congé commence l'après-midi du premier jour.
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(2), apresMidi: true) });

        // Assert
        jours.Count.Should().Be(3);
        jours[0].Valeur.Should().Be(0.5m);
        jours[0].Date.Should().Be(Lundi);
        jours[1].Valeur.Should().Be(1m);
        jours[2].Valeur.Should().Be(1m);
    }

    [Fact]
    public void Expandre_MatinPeriod_EndsWithHalfDay()
    {
        // Act
        // Le congé se termine le matin du dernier jour.
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(2), matin: true) });

        // Assert
        jours.Count.Should().Be(3);
        jours[0].Valeur.Should().Be(1m);
        jours[1].Valeur.Should().Be(1m);
        jours[2].Valeur.Should().Be(0.5m);
        jours[2].Date.Should().Be(Lundi.AddDays(2));
    }

    [Fact]
    public void Expandre_ApresMidiStartingOnSunday_StillAddsTheHalfDay()
    {
        // Arrange
        // Les bornes de demi-journée sont ajoutées sans filtrage du dimanche (comportement du desktop).
        var dimanche = Lundi.AddDays(6);

        // Act
        var jours = LeaveRules.Expandre(new[] { Periode(dimanche, dimanche.AddDays(1), apresMidi: true) });

        // Assert
        jours.Should().Contain(j => j.Date == dimanche && j.Valeur == 0.5m);
    }

    [Fact]
    public void Expandre_EmptyInput_ReturnsNothing()
    {
        // Act
        var result = LeaveRules.Expandre(Array.Empty<LeavePeriod>());

        // Assert
        result.Should().BeEmpty();
    }

    // ---- Interrogation ----

    [Fact]
    public void EstConge_DateWithTimePart_MatchesTheDay()
    {
        // Act
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi) });

        // Assert
        LeaveRules.EstConge(jours, Lundi.AddHours(9)).Should().BeTrue();
        LeaveRules.EstConge(jours, Lundi.AddHours(23)).Should().BeTrue();
        LeaveRules.EstConge(jours, Lundi.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Valeur_BoundaryOrOutsideDay_ReturnsHalfDayOrZero()
    {
        // Act
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(2), apresMidi: true) });

        // Assert
        LeaveRules.Valeur(jours, Lundi).Should().Be(0.5m);
        LeaveRules.Valeur(jours, Lundi.AddDays(1)).Should().Be(1m);
        LeaveRules.Valeur(jours, Lundi.AddDays(5)).Should().Be(0m);
    }

    [Fact]
    public void Valeur_OverlappingLeaves_KeepsStrongestValue()
    {
        // Act
        // Deux périodes se chevauchent le mardi : une demi-journée et une journée pleine.
        var jours = LeaveRules.Expandre(new[]
        {
            Periode(Lundi.AddDays(1), Lundi.AddDays(1), matin: true),
            Periode(Lundi.AddDays(1), Lundi.AddDays(1)),
        });

        // Assert
        LeaveRules.Valeur(jours, Lundi.AddDays(1)).Should().Be(1m);
    }
}
