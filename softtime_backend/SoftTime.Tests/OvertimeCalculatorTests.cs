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

        Assert.Equal(40m, resultat.Ht);
        Assert.Equal(0m, resultat.Hs);
        Assert.Equal(0m, resultat.Nuit);
    }

    [Fact]
    public void CasB_Semaine45h_Produit5hSupplementaires()
    {
        var config = Multi();
        var semaine = Semaine(j => JourneeOuvree(j, "08:00", "17:00", TimeSpan.Zero));

        var resultat = OvertimeCalculator.CalculerSemaine(config, semaine);

        Assert.Equal(45m, resultat.Ht);
        Assert.Equal(5m, resultat.Hs);
    }

    [Fact]
    public void CasC_Semaine46hAvecSamedi_Produit6hSupplementaires()
    {
        var config = Multi();
        var semaine = Semaine(j => JourneeOuvree(j, "08:00", "17:00", TimeSpan.Zero))
            .Append(Jour(Samedi, new[] { P(Samedi, "09:00", "10:00") }));

        var resultat = OvertimeCalculator.CalculerSemaine(config, semaine);

        Assert.Equal(46m, resultat.Ht);
        Assert.Equal(6m, resultat.Hs);
    }

    [Fact]
    public void CasF_NuitContre_QuatreHeuresDeNuit()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "20:00", "02:00", joursSortie: 1) });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(6m, resultat.HeuresReelles);
        Assert.Equal(5.5m, resultat.HeuresRetenues);
        Assert.Equal(4m, resultat.Nuit);
    }

    [Fact]
    public void CasG_Dimanche_CompteurDimancheSurHeuresRetenues()
    {
        var config = Multi();
        var jour = Jour(Dimanche, new[] { P(Dimanche, "08:00", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromHours(1));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(9m, resultat.HeuresReelles);
        Assert.Equal(8m, resultat.HeuresRetenues);
        Assert.Equal(8m, resultat.Dimanche);
        Assert.Equal(0m, resultat.Nuit);
    }

    [Fact]
    public void CasH_Ferie_CompteurFerieSurHeuresReelles()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "09:00", "15:00") }, ferie: true);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(6m, resultat.HeuresReelles);
        Assert.Equal(5.5m, resultat.HeuresRetenues);
        Assert.Equal(6m, resultat.Ferie);
    }

    [Fact]
    public void CasI_NuitUnDimanche_LaCotationNuitGagne()
    {
        // Samedi 22:00 → dimanche 04:00 : 6 h toutes comprises dans la plage 22:00 → 05:00.
        var config = Multi(cotationNuit: 3m, cotationDimanche: 2m);
        var jour = Jour(Samedi, new[] { P(Samedi, "22:00", "04:00", joursSortie: 1) });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(6m, resultat.Nuit);
        Assert.Equal(0m, resultat.Dimanche);
    }

    [Fact]
    public void CasI_NuitUnDimanche_LaCotationDimancheGagneSurLaPartieDimanche()
    {
        // La part de samedi (22:00 → 24:00) reste de la nuit ; la part de dimanche passe en dimanche.
        var config = Multi(cotationNuit: 1m, cotationDimanche: 2m);
        var jour = Jour(Samedi, new[] { P(Samedi, "22:00", "04:00", joursSortie: 1) });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(2m, resultat.Nuit);
        Assert.Equal(4m, resultat.Dimanche);
    }

    [Fact]
    public void CasJ_Conge_JourneeNeutraliseeSansAbsence()
    {
        var config = Multi();
        var jour = Jour(Lundi, Array.Empty<PunchPair>(), H("08:00"), H("17:00"), TimeSpan.FromHours(1), conge: 1m);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.True(resultat.Conge);
        Assert.Equal(8m, resultat.HeuresRetenues);
        Assert.Equal(0m, resultat.Absence);
        Assert.Equal(0m, resultat.Retard);
    }

    [Fact]
    public void CasJ_CongeDemiJournee_CrediteUneDemiJournee()
    {
        var config = Multi();
        var jour = Jour(Lundi, Array.Empty<PunchPair>(), H("08:00"), H("17:00"), TimeSpan.FromHours(1), conge: 0.5m);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(4m, resultat.HeuresRetenues);
    }

    [Fact]
    public void CasK_Absence_JourPlanifieSansPointage()
    {
        var config = Multi();
        var jour = Jour(Lundi, Array.Empty<PunchPair>(), H("08:00"), H("17:00"), TimeSpan.FromHours(1), correction: false);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.False(resultat.JourTravaille);
        Assert.Equal(0m, resultat.HeuresRetenues);
        Assert.Equal(8m, resultat.Absence);
    }

    [Fact]
    public void CasL_RetardToleranceAbsorbe_ToleranceDepassee()
    {
        var jour = Jour(Lundi, new[] { P(Lundi, "08:10", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromHours(1));

        var absorbe = OvertimeCalculator.CalculerJour(Multi(toleranceEntree: 10), jour);
        var depasse = OvertimeCalculator.CalculerJour(Multi(toleranceEntree: 5), jour);

        Assert.Equal(0m, absorbe.Retard);
        Assert.Equal(0.08m, depasse.Retard);
    }

    [Fact]
    public void CasL_RetardExplicite_Prioritaire()
    {
        var jour = Jour(Lundi, new[] { P(Lundi, "08:10", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromHours(1), retard: TimeSpan.FromMinutes(42));

        var resultat = OvertimeCalculator.CalculerJour(Multi(), jour);

        Assert.Equal(0.7m, resultat.Retard);
    }

    [Fact]
    public void CasM_ArriveeEnAvance_NonPayee()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "07:30", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromHours(1));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(H("07:30"), resultat.Entree);
        Assert.Equal(9m, resultat.HeuresReelles);
        Assert.Equal(8m, resultat.HeuresRetenues);
    }

    [Fact]
    public void CasN_SortieArrondieAuPasDeTolerance()
    {
        var config = Multi(pasSortie: 15);
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "17:50") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        // 17:50 → 17:45 : 9,75 h réelles, moins 30 min de pause manquante.
        Assert.Equal(9.75m, resultat.HeuresReelles);
        Assert.Equal(9.25m, resultat.HeuresRetenues);
    }

    [Fact]
    public void CasO_DeuxPaires_PauseReelleSuffisante()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "12:00"), P(Lundi, "13:00", "17:00") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(8m, resultat.HeuresReelles);
        Assert.Equal(8m, resultat.HeuresRetenues);
    }

    [Fact]
    public void PauseManquantePartielle_EstDeduite()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "12:00"), P(Lundi, "12:15", "17:00") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(8.75m, resultat.HeuresReelles);
        Assert.Equal(8.5m, resultat.HeuresRetenues);
    }

    [Fact]
    public void CasP_JourneeCourte_PasDeDeductionDePause()
    {
        var config = Multi();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "11:00") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(3m, resultat.HeuresReelles);
        Assert.Equal(3m, resultat.HeuresRetenues);
    }

    // ------------------------------------------------------------------ règles complémentaires (multi)

    [Fact]
    public void FerieSansTravail_CrediteLaJourneeSansMajoration()
    {
        var config = Multi();
        var jour = Jour(Lundi, Array.Empty<PunchPair>(), ferie: true, correction: false);

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(8m, resultat.HeuresRetenues);
        Assert.Equal(0m, resultat.Ferie);
        Assert.Equal(0m, resultat.Absence);
    }

    [Fact]
    public void Femme_8Mars_CrediteLaJourneeEtMajoreLesHeuresTravaillees()
    {
        var mars = new DateTime(2027, 3, 8); // lundi
        var jour = Jour(mars, new[] { P(mars, "09:00", "12:00") });

        var resultat = OvertimeCalculator.CalculerJour(Multi(estFemme: true), jour);

        Assert.Equal(8m, resultat.HeuresRetenues);
        Assert.Equal(3m, resultat.Ferie);
    }

    [Fact]
    public void Homme_8Mars_PasDeCredit()
    {
        var mars = new DateTime(2027, 3, 8);
        var jour = Jour(mars, new[] { P(mars, "09:00", "12:00") });

        var resultat = OvertimeCalculator.CalculerJour(Multi(estFemme: false), jour);

        Assert.Equal(3m, resultat.HeuresRetenues);
        Assert.Equal(0m, resultat.Ferie);
    }

    [Fact]
    public void FerieEtDimanche_LeFeriePrimed()
    {
        var jour = Jour(Dimanche, new[] { P(Dimanche, "08:00", "12:00") }, ferie: true);

        var resultat = OvertimeCalculator.CalculerJour(Multi(), jour);

        Assert.Equal(4m, resultat.Ferie);
        Assert.Equal(0m, resultat.Dimanche);
    }

    [Fact]
    public void SemaineVide_ProduitDesZeros()
    {
        var resultat = OvertimeCalculator.CalculerSemaine(Multi(), Array.Empty<OvertimeDayInput>());

        Assert.Equal(0m, resultat.Ht);
        Assert.Equal(0m, resultat.Hs);
        Assert.Empty(resultat.Jours);
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

        Assert.Equal(42.5m, resultat.Ht);
        Assert.Equal(0m, resultat.Hs);
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

        Assert.Equal(47.5m, resultat.Ht);
        Assert.Equal(5m, resultat.Hs);
    }

    [Fact]
    public void Mono_Samedi_IntegralementEnHeuresSup()
    {
        var config = Mono();
        var jour = Jour(Samedi, new[] { P(Samedi, "08:00", "12:00") }, H("08:00"), H("17:00"), TimeSpan.FromMinutes(30));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(3.5m, resultat.HeuresRetenues);
        Assert.Equal(3.5m, resultat.HeuresSup);
    }

    [Fact]
    public void Mono_SortieArrondieALaDemiHeureInferieure()
    {
        var config = Mono();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "17:40") }, H("08:00"), H("17:00"), TimeSpan.FromMinutes(30));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        // 17:40 → 17:30 (et non 17:40) : 9,5 h réelles.
        Assert.Equal(9.5m, resultat.HeuresReelles);
        Assert.Equal(9m, resultat.HeuresRetenues);
    }

    [Fact]
    public void Mono_Nuit_TrancheApres22h()
    {
        var config = Mono();
        var jour = Jour(Lundi, new[] { P(Lundi, "20:00", "23:00") }, H("20:00"), H("23:00"), TimeSpan.FromMinutes(30));

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(1m, resultat.Nuit);
        Assert.Equal(2.5m, resultat.HeuresRetenues);
    }

    [Fact]
    public void Mono_SansPlanning_ReconstruitLePrevuSurLesHeuresReelles()
    {
        var config = Mono();
        var jour = Jour(Lundi, new[] { P(Lundi, "08:00", "16:30") });

        var resultat = OvertimeCalculator.CalculerJour(config, jour);

        Assert.Equal(8m, resultat.HeuresRetenues);
        Assert.Equal(0m, resultat.HeuresSup);
    }

    [Fact]
    public void Mono_Dimanche_Et_Ferie_NeProduisentPasDHeuresSup()
    {
        var config = Mono();
        var dimanche = Jour(Dimanche, new[] { P(Dimanche, "08:00", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromMinutes(30));
        var ferie = Jour(Lundi, new[] { P(Lundi, "08:00", "17:00") }, H("08:00"), H("17:00"), TimeSpan.FromMinutes(30), ferie: true);

        var dimancheResultat = OvertimeCalculator.CalculerJour(config, dimanche);
        var ferieResultat = OvertimeCalculator.CalculerJour(config, ferie);

        Assert.Equal(8.5m, dimancheResultat.Dimanche);
        Assert.Equal(0m, dimancheResultat.HeuresSup);
        Assert.Equal(8.5m, ferieResultat.Ferie);
        Assert.Equal(0m, ferieResultat.HeuresSup);
    }
}
