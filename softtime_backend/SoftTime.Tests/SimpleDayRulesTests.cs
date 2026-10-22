using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Appariement du mode simple : comportement historique (défaut) et mode strict
/// (<c>SIMPLE_STRICT</c>), qui transforme les combinaisons inexploitables en anomalies motivées.
/// </summary>
public class SimpleDayRulesTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private static PunchMoment M(DateTime date, int h, int m = 0, int s = 0) =>
        new(date, new TimeSpan(h, m, s));

    private static SimpleDayOptions Strict(TimeSpan? min = null, TimeSpan? max = null) =>
        new(Strict: true, MinDuration: min, MaxDuration: max);

    // ---- Comportement historique (options par défaut) ----

    [Fact]
    public void Defaut_SansPointage_NeProduitRien()
    {
        var resultat = SimpleDayRules.Pair(Array.Empty<PunchMoment>());
        Assert.Empty(resultat.Pairs);
        Assert.Empty(resultat.Unpaired);
        Assert.Equal(PunchAnomalyMotifs.AucunPointage, resultat.Motif);
    }

    [Fact]
    public void Defaut_UnPointage_EstOrphelin()
    {
        var resultat = SimpleDayRules.Pair(new[] { M(Lundi, 8) });
        Assert.Empty(resultat.Pairs);
        Assert.Single(resultat.Unpaired);
        Assert.Equal(PunchAnomalyMotifs.PointageUnique, resultat.Motif);
    }

    [Fact]
    public void Defaut_DeuxPointages_PremiereEntreeDerniereSortie()
    {
        var resultat = SimpleDayRules.Pair(new[] { M(Lundi, 8), M(Lundi, 17) });
        var paire = Assert.Single(resultat.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), paire.EntreeHeure);
        Assert.Equal(new TimeSpan(17, 0, 0), paire.SortieHeure);
        Assert.Null(resultat.Motif);
        Assert.Null(resultat.Hps);
        Assert.Null(resultat.Hpe);
    }

    [Fact]
    public void Defaut_TroisPointages_ComportementHistoriqueInchange()
    {
        var resultat = SimpleDayRules.Pair(new[] { M(Lundi, 8), M(Lundi, 12), M(Lundi, 17) });
        var paire = Assert.Single(resultat.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), paire.EntreeHeure);
        Assert.Equal(new TimeSpan(17, 0, 0), paire.SortieHeure);
    }

    [Fact]
    public void Defaut_QuatrePointages_IgnoreLaPause()
    {
        var resultat = SimpleDayRules.Pair(new[] { M(Lundi, 8), M(Lundi, 12), M(Lundi, 13), M(Lundi, 17) });
        Assert.Single(resultat.Pairs);
        Assert.Null(resultat.Hps);
        Assert.Null(resultat.Hpe);
    }

    // ---- Mode strict ----

    [Fact]
    public void Strict_DeuxPointages_FormeUnePaire()
    {
        var resultat = SimpleDayRules.Pair(new[] { M(Lundi, 8), M(Lundi, 17) }, Strict());
        Assert.Single(resultat.Pairs);
        Assert.Null(resultat.Motif);
    }

    [Fact]
    public void Strict_QuatrePointages_LitLaPauseSurLesPointagesDuMilieu()
    {
        var resultat = SimpleDayRules.Pair(
            new[] { M(Lundi, 8), M(Lundi, 12), M(Lundi, 13), M(Lundi, 17) }, Strict());
        var paire = Assert.Single(resultat.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), paire.EntreeHeure);
        Assert.Equal(new TimeSpan(17, 0, 0), paire.SortieHeure);
        Assert.Equal(new TimeSpan(12, 0, 0), resultat.Hps);
        Assert.Equal(new TimeSpan(13, 0, 0), resultat.Hpe);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void Strict_NombreInexploitable_DevientAnomalie(int nombre)
    {
        var points = Enumerable.Range(0, nombre).Select(i => M(Lundi, 8 + i)).ToArray();
        var resultat = SimpleDayRules.Pair(points, Strict());
        Assert.Empty(resultat.Pairs);
        Assert.Equal(nombre, resultat.Unpaired.Count);
        Assert.Equal(PunchAnomalyMotifs.PointagesIncoherents, resultat.Motif);
    }

    [Fact]
    public void Strict_DoublonDeBadge_EstAbsorbe()
    {
        var resultat = SimpleDayRules.Pair(
            new[] { M(Lundi, 8, 0, 0), M(Lundi, 8, 0, 30), M(Lundi, 17) }, Strict());
        var paire = Assert.Single(resultat.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), paire.EntreeHeure);
        Assert.Equal(new TimeSpan(17, 0, 0), paire.SortieHeure);
    }

    [Fact]
    public void Strict_DureeAuDessusDuMaximum_DevientAnomalie()
    {
        var resultat = SimpleDayRules.Pair(
            new[] { M(Lundi, 8), M(Lundi, 17) }, Strict(max: TimeSpan.FromHours(8)));
        Assert.Empty(resultat.Pairs);
        Assert.Equal(PunchAnomalyMotifs.Duree, resultat.Motif);
    }

    [Fact]
    public void Strict_DureeEnDessousDuMinimum_DevientAnomalie()
    {
        var resultat = SimpleDayRules.Pair(
            new[] { M(Lundi, 8), M(Lundi, 9) }, Strict(min: TimeSpan.FromHours(4)));
        Assert.Empty(resultat.Pairs);
        Assert.Equal(PunchAnomalyMotifs.Duree, resultat.Motif);
    }

    [Fact]
    public void Strict_LaBornePorteSurLaDureeNetteDePause()
    {
        // 8 h → 17 h avec une heure de pause = 8 h nettes : la borne de 8 h passe,
        // celle de 7 h 59 échoue.
        var points = new[] { M(Lundi, 8), M(Lundi, 12), M(Lundi, 13), M(Lundi, 17) };
        Assert.Single(SimpleDayRules.Pair(points, Strict(max: TimeSpan.FromHours(8))).Pairs);
        Assert.Equal(PunchAnomalyMotifs.Duree,
            SimpleDayRules.Pair(points, Strict(max: new TimeSpan(7, 59, 0))).Motif);
    }

    [Fact]
    public void Strict_PointagesNonTries_SontOrdonnes()
    {
        var resultat = SimpleDayRules.Pair(new[] { M(Lundi, 17), M(Lundi, 8) }, Strict());
        var paire = Assert.Single(resultat.Pairs);
        Assert.Equal(new TimeSpan(8, 0, 0), paire.EntreeHeure);
        Assert.Equal(new TimeSpan(17, 0, 0), paire.SortieHeure);
    }

    [Fact]
    public void Strict_PosteDeNuit_SortieLeLendemain()
    {
        var resultat = SimpleDayRules.Pair(
            new[] { M(Lundi, 22), M(Lundi.AddDays(1), 6) }, Strict());
        var paire = Assert.Single(resultat.Pairs);
        Assert.Equal(Lundi.Date, paire.EntreeDate.Date);
        Assert.Equal(Lundi.AddDays(1).Date, paire.SortieDate.Date);
        Assert.Equal(new TimeSpan(22, 0, 0), paire.EntreeHeure);
        Assert.Equal(new TimeSpan(6, 0, 0), paire.SortieHeure);
    }
}
