#nullable enable

using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Réplique des jeux de tests de non-régression de la synthèse métier (SYNTHESE_CALCUL_HS.md §12),
/// plus les cas propres au mode 2 pointages. Hypothèses : semaine lundi → dimanche, catégorie à 40 h
/// (journée = 8 h), pause 30 min, aucun férié ni congé sauf mention.
/// </summary>
public class OvertimeCalculatorTests
{
    /// <summary>Lundi 31/08/2026 (même repère que les autres tests).</summary>
    private static readonly DateTime Lundi = new(2026, 8, 31);
    private static readonly DateTime Samedi = Lundi.AddDays(5);
    private static readonly DateTime Dimanche = Lundi.AddDays(6);

    private static TimeSpan H(string valeur) => TimeSpan.Parse(valeur);

    private static OvertimeWeekConfig Multi(
        decimal cotationNuit = 1m,
        decimal cotationDimanche = 2m,
        decimal cotationFerie = 3m,
        bool estFemme = false,
        int? pasSortie = null,
        int toleranceEntree = 0)
        => new(
            MultiPointage: true,
            HeuresSemaine: 40m,
            PauseCategorie: TimeSpan.FromMinutes(30),
            ToleranceEntreeMinutes: toleranceEntree,
            PasSortieMinutes: pasSortie,
            CotationNuit: cotationNuit,
            CotationDimanche: cotationDimanche,
            CotationFerie: cotationFerie,
            EstFemme: estFemme);

    private static OvertimeWeekConfig Mono(int toleranceEntree = 0)
        => Multi(toleranceEntree: toleranceEntree) with { MultiPointage = false };

    private static PunchPair P(DateTime jour, string entree, string sortie, int joursSortie = 0)
        => new(jour.Date, H(entree), jour.Date.AddDays(joursSortie), H(sortie));

    private static OvertimeDayInput Jour(
        DateTime jour,
        PunchPair[] paires,
        TimeSpan? ha = null,
        TimeSpan? hd = null,
        TimeSpan? pause = null,
        bool ferie = false,
        bool correction = true,
        decimal conge = 0m,
        TimeSpan? retard = null)
        => new(jour, paires, ha, hd, pause, Off: false, Ferie: ferie, CorrectionPresente: correction, CongeValeur: conge, RetardExplicite: retard);

    /// <summary>Journée type « 5 × N heures » avec planning 08:00–17:00 et pause planifiée.</summary>
    private static OvertimeDayInput JourneeOuvree(DateTime jour, string entree, string sortie, TimeSpan pausePlanifiee)
        => Jour(jour, new[] { P(jour, entree, sortie) }, H("08:00"), H("17:00"), pausePlanifiee);

    private static IEnumerable<OvertimeDayInput> Semaine(Func<DateTime, OvertimeDayInput> fabrique)
        => Enumerable.Range(0, 5).Select(i => fabrique(Lundi.AddDays(i)));

    // ------------------------------------------------------------------ synthèse §12 (multi)

    [Fact]
    public void CasA_SemaineNormale_SansHeuresSup()
    {
        var config = Multi();
        var semaine = Semaine(j => JourneeOuvree(j, "08:00", "17:00", TimeSpan.FromHours(1)));

        var resultat = OvertimeCalculator.CalculerSemaine(config, semaine);

        resultat.Ht.Should().Be(40m);
        resultat.Hs.Should().Be(0m);
        resultat.Nuit.Should().Be(0m);
    }

    [Fact]
    public void CasB_Semaine45h_Produit5hSupplementaires()
    {
        var config = Multi();
        var semaine = Semaine(j => JourneeOuvree(j, "08:00", "17:00", TimeSpan.Zero));

        var resultat = OvertimeCalculator.CalculerSemaine(config, semaine);

        resultat.Ht.Should().Be(45m);
        resultat.Hs.Should().Be(5m);
    }

    [Fact]
    public void CasC_Semaine46hAvecSamedi_Produit6hSupplementaires()
    {
        var config = Multi();
        var semaine = Semaine(j => JourneeOuvree(j, "08:00", "17:00", TimeSpan.Zero))
            .Append(Jour(Samedi, new[] { P(Samedi, "09:00", "10:00") }));

        var resultat = OvertimeCalculator.CalculerSemaine(config, semaine);

        resultat.Ht.Should().Be(46m);
        resultat.Hs.Should().Be(6m);
    }

    [Fact]
    public void CasF_NuitContre_QuatreHeuresDeNuit()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "20:00", "02:00", joursSortie: 1) });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresReelles.Should().Be(6m);
        resultat.HeuresRetenues.Should().Be(5.5m);
        resultat.Nuit.Should().Be(4m);
    }

    [Fact]
    public void CasG_Dimanche_CompteurDimancheSurHeuresRetenues()
    {
        var config = Multi();
        var jour = Jour(Dimanche, new[] { P(Dimanche, "08:00", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromHours(1));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresReelles.Should().Be(9m);
        resultat.HeuresRetenues.Should().Be(8m);
        resultat.Dimanche.Should().Be(8m);
        resultat.Nuit.Should().Be(0m);
    }

    [Fact]
    public void CasH_Ferie_CompteurFerieSurHeuresReelles()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "09:00", "15:00") }, ferie: true);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresReelles.Should().Be(6m);
        resultat.HeuresRetenues.Should().Be(5.5m);
        resultat.Ferie.Should().Be(6m);
    }

    [Fact]
    public void CasI_NuitUnDimanche_LaCotationNuitGagne()
    {
        // Samedi 22:00 → dimanche 04:00 : 6 h toutes comprises dans la plage 22:00 → 05:00.
        var config = Multi(cotationNuit: 3m, cotationDimanche: 2m);
        var jour = Jour(Samedi, new[] { P(Samedi, "22:00", "04:00", joursSortie: 1) });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.Nuit.Should().Be(6m);
        resultat.Dimanche.Should().Be(0m);
    }

    [Fact]
    public void CasI_NuitUnDimanche_LaCotationDimancheGagneSurLaPartieDimanche()
    {
        // La part de samedi (22:00 → 24:00) reste de la nuit ; la part de dimanche passe en dimanche.
        var config = Multi(cotationNuit: 1m, cotationDimanche: 2m);
        var jour = Jour(Samedi, new[] { P(Samedi, "22:00", "04:00", joursSortie: 1) });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.Nuit.Should().Be(2m);
        resultat.Dimanche.Should().Be(4m);
    }

    [Fact]
    public void CasJ_Conge_JourneeNeutraliseeSansAbsence()
    {
        var config = Multi();
        var jour = Jour(Lundi, Array.Empty<PunchPair>(), H("08:00"), H("17:00"), TimeSpan.FromHours(1), conge: 1m);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.Conge.Should().BeTrue();
        resultat.HeuresRetenues.Should().Be(8m);
        resultat.Absence.Should().Be(0m);
        resultat.Retard.Should().Be(0m);
    }

    [Fact]
    public void CasJ_CongeDemiJournee_CrediteUneDemiJournee()
    {
        var config = Multi();
        var jour = Jour(Lundi, Array.Empty<PunchPair>(), H("08:00"), H("17:00"), TimeSpan.FromHours(1), conge: 0.5m);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresRetenues.Should().Be(4m);
    }

    [Fact]
    public void CasK_Absence_JourPlanifieSansPointage()
    {
        var config = Multi();
        var jour = Jour(Lundi, Array.Empty<PunchPair>(), H("08:00"), H("17:00"), TimeSpan.FromHours(1), correction: false);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.JourTravaille.Should().BeFalse();
        resultat.HeuresRetenues.Should().Be(0m);
        resultat.Absence.Should().Be(8m);
    }

    [Fact]
    public void CasL_RetardToleranceAbsorbe_ToleranceDepassee()
    {
        var jour = Jour(Lundi, new[] { P(Lundi, "08:10", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromHours(1));

        var absorbe = OvertimeCalculator.CalculerJour(Multi(toleranceEntree: 10), jour);
        var depasse = OvertimeCalculator.CalculerJour(Multi(toleranceEntree: 5), jour);

        absorbe.Retard.Should().Be(0m);
        depasse.Retard.Should().Be(0.08m);
    }

    [Fact]
    public void CasL_RetardExplicite_Prioritaire()
    {
        var jour = Jour(Lundi, new[] { P(Lundi, "08:10", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromHours(1), retard: TimeSpan.FromMinutes(42));

        var resultat = OvertimeCalculator.CalculerJour(Multi(), jour);

        resultat.Retard.Should().Be(0.7m);
    }

    [Fact]
    public void CasM_ArriveeEnAvance_NonPayee()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "07:30", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromHours(1));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.Entree.Should().Be(H("07:30"));
        resultat.HeuresReelles.Should().Be(9m);
        resultat.HeuresRetenues.Should().Be(8m);
    }

    [Fact]
    public void CasN_SortieArrondieAuPasDeTolerance()
    {
        var config = Multi(pasSortie: 15);
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "17:50") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        // 17:50 → 17:45 : 9,75 h réelles, moins 30 min de pause manquante.
        resultat.HeuresReelles.Should().Be(9.75m);
        resultat.HeuresRetenues.Should().Be(9.25m);
    }

    [Fact]
    public void CasO_DeuxPaires_PauseReelleSuffisante()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "12:00"), P(Lundi, "13:00", "17:00") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresReelles.Should().Be(8m);
        resultat.HeuresRetenues.Should().Be(8m);
    }

    [Fact]
    public void PauseManquantePartielle_EstDeduite()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "12:00"), P(Lundi, "12:15", "17:00") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresReelles.Should().Be(8.75m);
        resultat.HeuresRetenues.Should().Be(8.5m);
    }

    [Fact]
    public void CasP_JourneeCourte_PasDeDeductionDePause()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "11:00") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresReelles.Should().Be(3m);
        resultat.HeuresRetenues.Should().Be(3m);
    }

    // ------------------------------------------------------------------ règles complémentaires (multi)

    [Fact]
    public void FerieSansTravail_CrediteLaJourneeSansMajoration()
    {
        var config = Multi();
        var jour = Jour(Lundi, Array.Empty<PunchPair>(), ferie: true, correction: false);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresRetenues.Should().Be(8m);
        resultat.Ferie.Should().Be(0m);
        resultat.Absence.Should().Be(0m);
    }

    [Fact]
    public void Femme_8Mars_CrediteLaJourneeEtMajoreLesHeuresTravaillees()
    {
        var mars = new DateTime(2027, 3, 8); // lundi
        var jour = Jour(mars, new[] { P(mars, "09:00", "12:00") });

        var resultat = OvertimeCalculator.CalculerJour(Multi(estFemme: true), jour);

        resultat.HeuresRetenues.Should().Be(8m);
        resultat.Ferie.Should().Be(3m);
    }

    [Fact]
    public void Homme_8Mars_PasDeCredit()
    {
        var mars = new DateTime(2027, 3, 8);
        var jour = Jour(mars, new[] { P(mars, "09:00", "12:00") });

        var resultat = OvertimeCalculator.CalculerJour(Multi(estFemme: false), jour);

        resultat.HeuresRetenues.Should().Be(3m);
        resultat.Ferie.Should().Be(0m);
    }

    [Fact]
    public void FerieEtDimanche_LeFeriePrimed()
    {
        var jour = Jour(Dimanche, new[] { P(Dimanche, "08:00", "12:00") }, ferie: true);

        var resultat = OvertimeCalculator.CalculerJour(Multi(), jour);

        resultat.Ferie.Should().Be(4m);
        resultat.Dimanche.Should().Be(0m);
    }

    [Fact]
    public void SemaineVide_ProduitDesZeros()
    {
        var resultat = OvertimeCalculator.CalculerSemaine(Multi(), Array.Empty<OvertimeDayInput>());

        resultat.Ht.Should().Be(0m);
        resultat.Hs.Should().Be(0m);
        resultat.Jours.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ mode 2 pointages

    private static OvertimeDayInput JourneeMono(DateTime jour, string entree, string sortie, TimeSpan? pause = null)
        => Jour(jour, new[] { P(jour, entree, sortie) }, H("08:00"), H("17:00"), pause ?? TimeSpan.FromMinutes(30));

    [Fact]
    public void Mono_SemaineNormale_SansHeuresSup()
    {
        var config = Mono();
        var semaine = Semaine(j => JourneeMono(j, "08:00", "17:00"));

        var resultat = OvertimeCalculator.CalculerSemaine(config, semaine);

        resultat.Ht.Should().Be(42.5m);
        resultat.Hs.Should().Be(0m);
    }

    [Fact]
    public void Mono_DefautDeJournee_ProduitDesHeuresSupJournalieres()
    {
        var config = Mono();
        // Planning 08:00–17:00, pause 30 min => prévu 8,5 h ; 10 h réalisées − 30 min = 9,5 h retenues.
        var semaine = Enumerable.Range(0, 5).Select(i =>
        {
            var jour = Lundi.AddDays(i);
            return Jour(jour, new[] { P(jour, "08:00", "18:00") }, H("08:00"), H("17:00"), TimeSpan.FromMinutes(30));
        });

        var resultat = OvertimeCalculator.CalculerSemaine(config, semaine);

        resultat.Ht.Should().Be(47.5m);
        resultat.Hs.Should().Be(5m);
    }

    [Fact]
    public void Mono_Samedi_IntegralementEnHeuresSup()
    {
        var config = Mono();
        var jour = Jour(Samedi, new[] { P(Samedi, "08:00", "12:00") }, H("08:00"), H("17:00"), TimeSpan.FromMinutes(30));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresRetenues.Should().Be(3.5m);
        resultat.HeuresSup.Should().Be(3.5m);
    }

    [Fact]
    public void Mono_SortieArrondieALaDemiHeureInferieure()
    {
        var config = Mono();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "17:40") }, H("08:00"), H("17:00"), TimeSpan.FromMinutes(30));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        // 17:40 → 17:30 (et non 17:40) : 9,5 h réelles.
        resultat.HeuresReelles.Should().Be(9.5m);
        resultat.HeuresRetenues.Should().Be(9m);
    }

    [Fact]
    public void Mono_Nuit_TrancheApres22h()
    {
        var config = Mono();
        var jour = Jour(Lundi, new[] { P(Lundi, "20:00", "23:00") }, H("20:00"), H("23:00"), TimeSpan.FromMinutes(30));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.Nuit.Should().Be(1m);
        resultat.HeuresRetenues.Should().Be(2.5m);
    }

    [Fact]
    public void Mono_SansPlanning_ReconstruitLePrevuSurLesHeuresReelles()
    {
        var config = Mono();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "16:30") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        resultat.HeuresRetenues.Should().Be(8m);
        resultat.HeuresSup.Should().Be(0m);
    }

    [Fact]
    public void Mono_Dimanche_Et_Ferie_NeProduisentPasDHeuresSup()
    {
        var config = Mono();
        var dimanche = Jour(Dimanche, new[] { P(Dimanche, "08:00", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromMinutes(30));
        var ferie = Jour(Lundi, new[] { P(Lundi, "08:00", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromMinutes(30), ferie: true);

        var dimancheResultat = OvertimeCalculator.CalculerJour(config, dimanche);
        var ferieResultat = OvertimeCalculator.CalculerJour(config, ferie);

        dimancheResultat.Dimanche.Should().Be(8.5m);
        dimancheResultat.HeuresSup.Should().Be(0m);
        ferieResultat.Ferie.Should().Be(8.5m);
        ferieResultat.HeuresSup.Should().Be(0m);
    }
}
