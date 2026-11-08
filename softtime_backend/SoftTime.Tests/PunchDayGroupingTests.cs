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

        resultat[Lundi].Count.Should().Be(2);
        resultat[mardi].Should().ContainSingle();
        resultat[Lundi][0].Heure.Should().Be(new TimeSpan(8, 0, 0));
        resultat[Lundi][1].Heure.Should().Be(new TimeSpan(17, 0, 0));
    }

    [Fact]
    public void JourneeSimple_UtiliseTousLesPointagesDuJour()
    {
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(8), Lundi.AddHours(17), false));
        var pointages = new[] { M(Lundi, 7, 55), M(Lundi, 12), M(Lundi, 17, 5) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, Lundi);

        resultat[Lundi].Count.Should().Be(3);
    }

    [Fact]
    public void Nuit_RattacheLaSortieAuJourDEntree()
    {
        var mardi = Lundi.AddDays(1);
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(22), mardi.AddHours(6), false));
        var pointages = new[] { M(Lundi, 22), M(mardi, 6) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, mardi);

        resultat[Lundi].Count.Should().Be(2);
        resultat[mardi].Should().BeEmpty();
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

        resultat[Lundi].Count.Should().Be(2);
        resultat[Lundi][0].Heure.Should().Be(new TimeSpan(22, 0, 0));
        resultat[Lundi][1].Heure.Should().Be(new TimeSpan(7, 0, 0));
        resultat[mardi].Count.Should().Be(2);
        resultat[mardi][0].Heure.Should().Be(new TimeSpan(8, 0, 0));
        resultat[mardi][1].Heure.Should().Be(new TimeSpan(17, 0, 0));
    }

    [Fact]
    public void Nuit_SansPlanningSuivant_UtiliseLaMargeDeSortie()
    {
        var mardi = Lundi.AddDays(1);
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(22), mardi.AddHours(6), false));
        var pointages = new[] { M(Lundi, 22), M(mardi, 6), M(mardi, 11) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, mardi);

        resultat[Lundi].Count.Should().Be(2);
        resultat[mardi].Should().ContainSingle();
        resultat[mardi][0].Heure.Should().Be(new TimeSpan(11, 0, 0));
    }

    [Fact]
    public void Nuit_SaisieSurLaMemeDate_EstReconnue()
    {
        var mardi = Lundi.AddDays(1);
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(22), Lundi.AddHours(6), false));
        var pointages = new[] { M(Lundi, 22), M(mardi, 6) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, mardi);

        resultat[Lundi].Count.Should().Be(2);
        resultat[mardi].Should().BeEmpty();
    }

    [Fact]
    public void OffP_NeRegroupePasLaNuit()
    {
        var mardi = Lundi.AddDays(1);
        var fenetres = Planifier(new PunchPlanningWindow(
            Lundi, Lundi.AddHours(22), mardi.AddHours(6), true));
        var pointages = new[] { M(Lundi, 22), M(mardi, 6) };

        var resultat = PunchDayGrouping.Build(pointages, fenetres, Lundi, mardi);

        resultat[Lundi].Should().ContainSingle();
        resultat[mardi].Should().ContainSingle();
    }

    [Fact]
    public void PointagesNonTries_SontTriesParDateEtHeure()
    {
        var pointages = new[] { M(Lundi, 17), M(Lundi, 8), M(Lundi, 12) };

        var resultat = PunchDayGrouping.Build(pointages, Planifier(), Lundi, Lundi);

        resultat[Lundi].Select(p => p.Heure.Hours).Should().Equal(new[] { 8, 12, 17 });
    }
}
