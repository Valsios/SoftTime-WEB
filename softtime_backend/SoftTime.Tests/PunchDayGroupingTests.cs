using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class PunchDayGroupingTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private static PunchMoment M(DateTime date, int h, int m = 0) => new(date, new TimeSpan(h, m, 0));

    private static Dictionary<DateTime, PunchPlanningWindow> Planifier(params PunchPlanningWindow[] fenetres)
        => fenetres.ToDictionary(f => f.Jour);

    [Fact]
    public void SansPlanning_GardeLesPointagesDeChaqueJour()
    {
        var mardi = Lundi.AddDays(1);
        var pointages = new[] { M(Lundi, 8), M(Lundi, 17), M(mardi, 8) };

        var resultat = PunchDayGrouping.Build(pointages, Planifier(), Lundi, mardi);

        Assert.Equal(2, resultat[Lundi].Count);
        Assert.Single(resultat[mardi]);
        Assert.Equal(new TimeSpan(8, 0, 0), resultat[Lundi][0].Heure);
        Assert.Equal(new TimeSpan(17, 0, 0), resultat[Lundi][1].Heure);
    }

    [Fact]
    public void JourneeSimple_UtiliseTousLesPointagesDuJour()
    {
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(8), Lundi.AddHours(17), false));
        var pointages = new[] { M(Lundi, 7, 55), M(Lundi, 12), M(Lundi, 17, 5) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, Lundi);

        Assert.Equal(3, resultat[Lundi].Count);
    }

    [Fact]
    public void Nuit_RattacheLaSortieAuJourDEntree()
    {
        var mardi = Lundi.AddDays(1);
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(22), mardi.AddHours(6), false));
        var pointages = new[] { M(Lundi, 22), M(mardi, 6) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, mardi);

        Assert.Equal(2, resultat[Lundi].Count);
        Assert.Empty(resultat[mardi]);
    }

    [Fact]
    public void Nuit_LePlanningDuLendemainBorneLaSortie()
    {
        var mardi = Lundi.AddDays(1);
        var fenetres = Planifier(
            new PunchPlanningWindow(Lundi, Lundi.AddHours(22), mardi.AddHours(6), false),
            new PunchPlanningWindow(mardi, mardi.AddHours(8), mardi.AddHours(17), false));
        var pointages = new[]
        {
            M(Lundi, 22),
            M(mardi, 7),
            M(mardi, 8),
            M(mardi, 17)
        };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, mardi);

        Assert.Equal(2, resultat[Lundi].Count);
        Assert.Equal(new TimeSpan(22, 0, 0), resultat[Lundi][0].Heure);
        Assert.Equal(new TimeSpan(7, 0, 0), resultat[Lundi][1].Heure);
        Assert.Equal(2, resultat[mardi].Count);
        Assert.Equal(new TimeSpan(8, 0, 0), resultat[mardi][0].Heure);
        Assert.Equal(new TimeSpan(17, 0, 0), resultat[mardi][1].Heure);
    }

    [Fact]
    public void Nuit_SansPlanningSuivant_UtiliseLaMargeDeSortie()
    {
        var mardi = Lundi.AddDays(1);
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(22), mardi.AddHours(6), false));
        var pointages = new[] { M(Lundi, 22), M(mardi, 6), M(mardi, 11) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, mardi);

        Assert.Equal(2, resultat[Lundi].Count);
        Assert.Single(resultat[mardi]);
        Assert.Equal(new TimeSpan(11, 0, 0), resultat[mardi][0].Heure);
    }

    [Fact]
    public void Nuit_SaisieSurLaMemeDate_EstReconnue()
    {
        var mardi = Lundi.AddDays(1);
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(22), Lundi.AddHours(6), false));
        var pointages = new[] { M(Lundi, 22), M(mardi, 6) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, mardi);

        Assert.Equal(2, resultat[Lundi].Count);
        Assert.Empty(resultat[mardi]);
    }

    [Fact]
    public void OffP_NeRegroupePasLaNuit()
    {
        var mardi = Lundi.AddDays(1);
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(22), mardi.AddHours(6), true));
        var pointages = new[] { M(Lundi, 22), M(mardi, 6) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, mardi);

        Assert.Single(resultat[Lundi]);
        Assert.Single(resultat[mardi]);
    }

    [Fact]
    public void PointagesNonTries_SontTriesParDateEtHeure()
    {
        var pointages = new[] { M(Lundi, 17), M(Lundi, 8), M(Lundi, 12) };

        var resultat = PunchDayGrouping.Build(pointages, Planifier(), Lundi, Lundi);

        Assert.Equal(new[] { 8, 12, 17 }, resultat[Lundi].Select(p => p.Heure.Hours));
    }
}
