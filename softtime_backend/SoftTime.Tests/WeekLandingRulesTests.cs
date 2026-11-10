#nullable enable

using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Atterrissage de semaine. Hypothèses : semaine lundi → dimanche, catégorie à 40 h, planning
/// 08:00–17:00 avec 1 h de pause (8 h créditées par jour), aucun férié ni congé sauf mention.
/// </summary>
public class WeekLandingRulesTests
{
    /// <summary>Lundi 31/08/2026 (même repère que les tests du calcul HS).</summary>
    private static readonly DateTime Lundi = new(2026, 8, 31);
    private static readonly DateTime Jeudi = Lundi.AddDays(3);
    private static readonly DateTime Vendredi = Lundi.AddDays(4);
    private static readonly DateTime Samedi = Lundi.AddDays(5);

    private static readonly OvertimeWeekConfig Multi = new(
        MultiPointage: true, HeuresSemaine: 40m, PauseCategorie: TimeSpan.FromMinutes(30));

    private static readonly OvertimeWeekConfig Mono = Multi with { MultiPointage = false };

    private static TimeSpan H(string valeur) => TimeSpan.Parse(valeur);

    /// <summary>Jour planifié 08:00–17:00, pointé de 08:00 à <paramref name="sortie"/> (non pointé si absent).</summary>
    private static OvertimeDayInput Jour(DateTime date, string? sortie = null, decimal conge = 0m)
    {
        var paires = sortie is null
            ? Array.Empty<PunchPair>()
            : new[] { new PunchPair(date, H("08:00"), date, H(sortie)) };
        return new OvertimeDayInput(
            date, paires, H("08:00"), H("17:00"), TimeSpan.FromHours(1),
            CorrectionPresente: sortie is not null, CongeValeur: conge);
    }

    /// <summary>Semaine de 5 jours : les <paramref name="pointes"/> premiers sont pointés jusqu'à <paramref name="sortie"/>.</summary>
    private static List<OvertimeDayInput> Semaine(int pointes, string sortie, DateTime? lundi = null)
        => Enumerable.Range(0, 5)
            .Select(i => Jour((lundi ?? Lundi).AddDays(i), i < pointes ? sortie : null))
            .ToList();

    // ------------------------------------------------------------------ atterrissage

    [Fact]
    public void Calculer_MiSemaine_AjouteLePlanningRestantAuxHeuresFaites()
    {
        // Arrange : 3 jours de 10 h pointés, 2 jours de 8 h encore planifiés.
        var semaine = Semaine(pointes: 3, sortie: "19:00");

        // Act
        var resultat = WeekLandingRules.Calculer(Multi, semaine, Jeudi, WeekDrift.Aucune);

        // Assert
        resultat.HeuresRealisees.Should().Be(30m);
        resultat.HsAcquises.Should().Be(0m);
        resultat.HeuresPlanifieesRestantes.Should().Be(16m);
        resultat.HsSelonPlanning.Should().Be(6m);
        resultat.HsAtterrissage.Should().Be(6m);
        resultat.Niveau.Should().Be(LandingLevel.HeuresSup);
        resultat.HeuresARetirer.Should().Be(6m);
        resultat.DejaFranchi.Should().BeFalse();
    }

    [Fact]
    public void Calculer_AtterrissageAuDelaDeHuitHeures_SignaleLePassageA150EtLesHeuresARetirer()
    {
        // Arrange : 3 jours de 12 h (36 h) + 16 h planifiées = 52 h, soit 12 h supplémentaires.
        var semaine = Semaine(pointes: 3, sortie: "21:00");

        // Act
        var resultat = WeekLandingRules.Calculer(Multi, semaine, Jeudi, WeekDrift.Aucune);

        // Assert
        resultat.HsAtterrissage.Should().Be(12m);
        resultat.Niveau.Should().Be(LandingLevel.Majoration150);
        resultat.Seuil.Should().Be(OvertimeRules.WeeklyHs130Cap);
        resultat.HeuresARetirer.Should().Be(4m);
        resultat.DejaFranchi.Should().BeFalse();
    }

    [Fact]
    public void Calculer_SeuilDejaFranchiParLesHeuresFaites_NEstPlusEvitable()
    {
        // Arrange : 4 jours de 13 h (52 h) déjà pointés, le seuil de 8 h est derrière.
        var semaine = Semaine(pointes: 4, sortie: "22:00");

        // Act
        var resultat = WeekLandingRules.Calculer(Multi, semaine, Vendredi, WeekDrift.Aucune);

        // Assert
        resultat.HsAcquises.Should().Be(12m);
        resultat.HsAtterrissage.Should().Be(20m);
        resultat.Niveau.Should().Be(LandingLevel.Majoration150);
        resultat.DejaFranchi.Should().BeTrue();
        resultat.HeuresARetirer.Should().Be(0m);
    }

    [Fact]
    public void Calculer_SemaineTerminee_RendLeReelSansProjection()
    {
        // Arrange
        var semaine = Semaine(pointes: 5, sortie: "19:00");

        // Act
        var resultat = WeekLandingRules.Calculer(Multi, semaine, Lundi.AddDays(7), new WeekDrift(5m, 5m, 4));

        // Assert : plus rien à planifier, l'écart habituel ne s'applique plus.
        resultat.HeuresPlanifieesRestantes.Should().Be(0m);
        resultat.Derive.Should().Be(0m);
        resultat.HsAtterrissage.Should().Be(resultat.HsAcquises).And.Be(10m);
    }

    [Fact]
    public void Calculer_SemaineFuturePlanifieeAuContrat_NAnnonceAucuneHeureSup()
    {
        // Arrange
        var semaine = Semaine(pointes: 0, sortie: "17:00");

        // Act
        var resultat = WeekLandingRules.Calculer(Multi, semaine, Lundi, WeekDrift.Aucune);

        // Assert
        resultat.HeuresRealisees.Should().Be(0m);
        resultat.HeuresPlanifieesRestantes.Should().Be(40m);
        resultat.HsAtterrissage.Should().Be(0m);
        resultat.Niveau.Should().Be(LandingLevel.Aucun);
    }

    [Fact]
    public void Calculer_JourRestantSansPlanning_EstSignaleEtNonCompte()
    {
        // Arrange : le jeudi n'a ni planning ni pointage.
        var semaine = Semaine(pointes: 3, sortie: "19:00");
        semaine[3] = new OvertimeDayInput(Jeudi, Array.Empty<PunchPair>());

        // Act
        var resultat = WeekLandingRules.Calculer(Multi, semaine, Jeudi, WeekDrift.Aucune);

        // Assert
        resultat.JoursSansPlanning.Should().Be(1);
        resultat.HeuresPlanifieesRestantes.Should().Be(8m);
        resultat.HsAtterrissage.Should().Be(0m);
    }

    [Fact]
    public void Calculer_CongeRestant_EstCrediteSansEtreUneHeureARetirer()
    {
        // Arrange : congé SAGE le jeudi (journée créditée), vendredi planifié.
        var semaine = Semaine(pointes: 3, sortie: "19:00");
        semaine[3] = Jour(Jeudi, conge: 1m);

        // Act
        var resultat = WeekLandingRules.Calculer(Multi, semaine, Jeudi, WeekDrift.Aucune);

        // Assert : 30 h faites + 8 h de congé + 8 h planifiées = 46 h.
        resultat.HeuresPlanifieesRestantes.Should().Be(8m);
        resultat.JoursSansPlanning.Should().Be(0);
        resultat.HsAtterrissage.Should().Be(6m);
    }

    [Fact]
    public void Calculer_EcartHabituel_SAppliqueAuProrataDesHeuresRestantes()
    {
        // Arrange : +5 h par semaine d'habitude ; il reste 16 h planifiées sur 40, soit +2 h.
        var semaine = Semaine(pointes: 3, sortie: "19:00");

        // Act
        var resultat = WeekLandingRules.Calculer(Multi, semaine, Jeudi, new WeekDrift(5m, 0m, 4));

        // Assert
        resultat.Derive.Should().Be(2m);
        resultat.HsSelonPlanning.Should().Be(6m);
        resultat.HsAtterrissage.Should().Be(8m);
    }

    [Fact]
    public void Calculer_EcartHabituelNegatif_NeFaitJamaisPasserSousLesHeuresAcquises()
    {
        // Arrange : 12 h supplémentaires déjà acquises, un salarié qui fait d'habitude moins que prévu.
        var semaine = Semaine(pointes: 4, sortie: "22:00");

        // Act
        var resultat = WeekLandingRules.Calculer(Multi, semaine, Vendredi, new WeekDrift(-100m, -100m, 4));

        // Assert
        resultat.HsAtterrissage.Should().Be(12m);
    }

    [Fact]
    public void Calculer_ModeDeuxPointages_SamediPlanifieCompteEntierementEnHeuresSup()
    {
        // Arrange : semaine au planning, puis un samedi planifié 08:00–12:00 sans pause.
        var semaine = Semaine(pointes: 5, sortie: "17:00");
        semaine.Add(new OvertimeDayInput(Samedi, Array.Empty<PunchPair>(), H("08:00"), H("12:00"), TimeSpan.Zero));

        // Act
        var resultat = WeekLandingRules.Calculer(Mono, semaine, Samedi, WeekDrift.Aucune);

        // Assert
        resultat.HsAcquises.Should().Be(0m);
        resultat.HsAtterrissage.Should().Be(4m);
        resultat.HeuresARetirer.Should().Be(4m);
    }

    // ------------------------------------------------------------------ écart habituel

    [Fact]
    public void Deriver_SemainesPlusLonguesQuePrevu_RendLEcartMoyen()
    {
        // Arrange : deux semaines à 9 h par jour au lieu de 8 h, soit +5 h chacune.
        var semaines = new[] { Semaine(5, "18:00", Lundi.AddDays(-7)), Semaine(5, "18:00", Lundi.AddDays(-14)) };

        // Act
        var derive = WeekLandingRules.Deriver(Multi, semaines);

        // Assert
        derive.Heures.Should().Be(5m);
        derive.HeuresSup.Should().Be(5m);
        derive.Semaines.Should().Be(2);
    }

    [Fact]
    public void Deriver_SemaineSansPointage_EstIgnoree()
    {
        // Arrange : une semaine non importée ne doit pas passer pour cinq absences.
        var semaines = new[] { Semaine(0, "17:00", Lundi.AddDays(-7)), Semaine(5, "18:00", Lundi.AddDays(-14)) };

        // Act
        var derive = WeekLandingRules.Deriver(Multi, semaines);

        // Assert
        derive.Heures.Should().Be(5m);
        derive.Semaines.Should().Be(1);
    }

    [Fact]
    public void Deriver_SansHistorique_RendUnEcartNul()
    {
        // Act
        var derive = WeekLandingRules.Deriver(Multi, Array.Empty<IReadOnlyList<OvertimeDayInput>>());

        // Assert
        derive.Should().Be(WeekDrift.Aucune);
    }

    // ------------------------------------------------------------------ briques

    [Theory]
    [InlineData(0, LandingLevel.Aucun, 0)]
    [InlineData(0.5, LandingLevel.HeuresSup, 0)]
    [InlineData(8, LandingLevel.HeuresSup, 0)]
    [InlineData(8.25, LandingLevel.Majoration150, 8)]
    [InlineData(20, LandingLevel.Majoration150, 8)]
    [InlineData(20.5, LandingLevel.Plafond, 20)]
    public void Niveau_SelonLeTotalHebdomadaire_RendLeSeuilFranchi(double heuresSup, LandingLevel niveau, double seuil)
    {
        // Act
        var resultat = WeekLandingRules.Niveau((decimal)heuresSup);

        // Assert
        resultat.Should().Be((niveau, (decimal)seuil));
    }

    [Fact]
    public void Planifier_PosteDeNuit_FaitSortirLeLendemain()
    {
        // Arrange
        var jour = new OvertimeDayInput(Lundi, Array.Empty<PunchPair>(), H("22:00"), H("06:00"));

        // Act
        var planifie = WeekLandingRules.Planifier(jour);

        // Assert
        planifie.Paires.Should().ContainSingle()
            .Which.Should().Be(new PunchPair(Lundi, H("22:00"), Lundi.AddDays(1), H("06:00")));
    }

    [Fact]
    public void Planifier_JourDeRepos_ResteInchange()
    {
        // Arrange
        var jour = new OvertimeDayInput(Lundi, Array.Empty<PunchPair>(), H("08:00"), H("17:00"), Off: true);

        // Act
        var planifie = WeekLandingRules.Planifier(jour);

        // Assert
        planifie.Should().BeSameAs(jour);
    }
}
