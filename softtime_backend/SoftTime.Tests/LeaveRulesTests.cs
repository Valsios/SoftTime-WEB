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
    public void EstCodeConge_AcceptsRegularCodes(string code)
        => Assert.True(LeaveRules.EstCodeConge(code));

    [Theory]
    [InlineData("0200")]
    [InlineData("0300")]
    [InlineData(" 0200 ")]
    [InlineData("")]
    [InlineData(null)]
    public void EstCodeConge_RejectsExcludedOrEmptyCodes(string? code)
        => Assert.False(LeaveRules.EstCodeConge(code));

    // ---- Période d'une seule journée ----

    [Fact]
    public void Expandre_SingleDayWithoutFlag_CountsFullDay()
    {
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi) });
        var jour = Assert.Single(jours);
        Assert.Equal(Lundi, jour.Date);
        Assert.Equal(1m, jour.Valeur);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Expandre_SingleDayWithHalfDayFlag_CountsHalfDay(bool matin, bool apresMidi)
    {
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi, matin, apresMidi) });
        Assert.Equal(0.5m, Assert.Single(jours).Valeur);
    }

    // ---- Périodes de plusieurs jours ----

    [Fact]
    public void Expandre_MultiDayWithoutFlag_CountsEveryDay()
    {
        // Lundi → mercredi : 3 journées pleines.
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(2)) });
        Assert.Equal(3, jours.Count);
        Assert.All(jours, j => Assert.Equal(1m, j.Valeur));
    }

    [Fact]
    public void Expandre_NeverCountsSundays()
    {
        // Lundi → dimanche inclus : le dimanche est exclu, il reste 6 jours.
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(6)) });
        Assert.Equal(6, jours.Count);
        Assert.DoesNotContain(jours, j => j.Date.DayOfWeek == DayOfWeek.Sunday);
    }

    [Fact]
    public void Expandre_ApresMidiPeriod_StartsWithHalfDay()
    {
        // Le congé commence l'après-midi du premier jour.
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(2), apresMidi: true) });
        Assert.Equal(3, jours.Count);
        Assert.Equal(0.5m, jours[0].Valeur);
        Assert.Equal(Lundi, jours[0].Date);
        Assert.Equal(1m, jours[1].Valeur);
        Assert.Equal(1m, jours[2].Valeur);
    }

    [Fact]
    public void Expandre_MatinPeriod_EndsWithHalfDay()
    {
        // Le congé se termine le matin du dernier jour.
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(2), matin: true) });
        Assert.Equal(3, jours.Count);
        Assert.Equal(1m, jours[0].Valeur);
        Assert.Equal(1m, jours[1].Valeur);
        Assert.Equal(0.5m, jours[2].Valeur);
        Assert.Equal(Lundi.AddDays(2), jours[2].Date);
    }

    [Fact]
    public void Expandre_ApresMidiStartingOnSunday_StillAddsTheHalfDay()
    {
        // Les bornes de demi-journée sont ajoutées sans filtrage du dimanche (comportement du desktop).
        var dimanche = Lundi.AddDays(6);
        var jours = LeaveRules.Expandre(new[] { Periode(dimanche, dimanche.AddDays(1), apresMidi: true) });
        Assert.Contains(jours, j => j.Date == dimanche && j.Valeur == 0.5m);
    }

    [Fact]
    public void Expandre_EmptyInputYieldsNothing()
        => Assert.Empty(LeaveRules.Expandre(Array.Empty<LeavePeriod>()));

    // ---- Interrogation ----

    [Fact]
    public void EstConge_MatchesTheDayWhateverTheTimePart()
    {
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi) });
        Assert.True(LeaveRules.EstConge(jours, Lundi.AddHours(9)));
        Assert.True(LeaveRules.EstConge(jours, Lundi.AddHours(23)));
        Assert.False(LeaveRules.EstConge(jours, Lundi.AddDays(1)));
    }

    [Fact]
    public void Valeur_ReturnsTheHalfDayOnBoundariesAndZeroOutside()
    {
        var jours = LeaveRules.Expandre(new[] { Periode(Lundi, Lundi.AddDays(2), apresMidi: true) });
        Assert.Equal(0.5m, LeaveRules.Valeur(jours, Lundi));
        Assert.Equal(1m, LeaveRules.Valeur(jours, Lundi.AddDays(1)));
        Assert.Equal(0m, LeaveRules.Valeur(jours, Lundi.AddDays(5)));
    }

    [Fact]
    public void Valeur_KeepsTheStrongestValueOnOverlap()
    {
        // Deux périodes se chevauchent le mardi : une demi-journée et une journée pleine.
        var jours = LeaveRules.Expandre(new[]
        {
            Periode(Lundi.AddDays(1), Lundi.AddDays(1), matin: true),
            Periode(Lundi.AddDays(1), Lundi.AddDays(1)),
        });
        Assert.Equal(1m, LeaveRules.Valeur(jours, Lundi.AddDays(1)));
    }
}
