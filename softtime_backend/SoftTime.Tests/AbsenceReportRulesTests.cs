using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

public class AbsenceReportRulesTests
{
    private static readonly DateTime Today = new(2025, 11, 20); // Hier = 2025-11-19

    private static T_CARDPAIE Carte(int id, string matricule, string? nom = "Dupont", string? prenom = "Jean", string? dep = "INFO")
        => new() { ID = id, SAGE_MATRICULE = matricule, SAGE_NOM = nom, SAGE_PRENOM = prenom, DEPARTEMENT = dep };

    private static T_PLANNING_SAL Plan(int id, int idsal, DateTime date, TimeSpan? ha = null, TimeSpan? hd = null, TimeSpan? pause = null, bool offP = false)
        => new()
        {
            ID = id,
            IDSAL = idsal,
            DateP = date,
            HA = ha.HasValue ? date.Date.Add(ha.Value) : null,
            HD = hd.HasValue ? date.Date.Add(hd.Value) : null,
            Pause = pause,
            OffP = offP
        };

    private static T_HEURECORRIGER Corr(int id, string matricule, DateTime date, bool am = false, bool pm = false, string? intitule = null)
        => new()
        {
            ID = id,
            MATRICULE_SAGE = matricule,
            DATE_POINTAGE_IN = date,
            ABSENCE_AM = am,
            ABSENCE_PM = pm,
            INTITULE_ABSENCE = intitule,
            VALIDER_CORRECTION = true
        };

    [Fact]
    public void Calculer_ListeVide_RetourneVideSansException()
    {
        var result = AbsenceReportRules.Calculer(
            Array.Empty<T_PLANNING_SAL>(),
            Array.Empty<T_CARDPAIE>(),
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            new DateTime(2025, 11, 1),
            new DateTime(2025, 11, 15),
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_FromPosterieurALaFinEffective_RetourneVideSansException()
    {
        // Période demandée dans le futur (ex: après hier)
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, new DateTime(2025, 11, 25), TimeSpan.FromHours(8), TimeSpan.FromHours(17)) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            new DateTime(2025, 11, 22),
            new DateTime(2025, 11, 30),
            Today); // hier = 2025-11-19

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_JourPlanifieSansCorrection_GenereNonJustifiee()
    {
        // Mardi 04/11/2025 : planifié de 08:00 à 17:00 avec 1h pause (8h)
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001", "Martin", "Paul", "RH") };
        var plannings = new[] { Plan(10, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(17), TimeSpan.FromHours(1)) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            new[] { Corr(1, "001", date.AddDays(-1)) }, // Autre jour pointé : ne masque plus l'absence
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("001", row.Matricule);
        Assert.Equal("Martin", row.Nom);
        Assert.Equal("Paul", row.Prenom);
        Assert.Equal("RH", row.Departement);
        Assert.Equal(date, row.Date);
        Assert.Equal("Journée", row.Periode);
        Assert.Equal(8m, row.DureeHeures);
        Assert.Equal("Non justifiée", row.Type);
        Assert.Null(row.Motif);
    }

    [Fact]
    public void Calculer_JourOffP_NeGenereAucuneLigne()
    {
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(10, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(17), offP: true) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_WeekEndEtFerie_SontExclusDeTousLesTypes()
    {
        // Samedi 08/11/2025 et Dimanche 09/11/2025
        var samedi = new DateTime(2025, 11, 8);
        var dimanche = new DateTime(2025, 11, 9);
        var ferie = new DateTime(2025, 11, 11); // Mardi férié

        var cartes = new[] { Carte(1, "001") };
        var plannings = new[]
        {
            Plan(1, 1, samedi, TimeSpan.FromHours(8), TimeSpan.FromHours(16)),
            Plan(2, 1, dimanche, TimeSpan.FromHours(8), TimeSpan.FromHours(16)),
            Plan(3, 1, ferie, TimeSpan.FromHours(8), TimeSpan.FromHours(16))
        };

        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["1"] = new() { new LeaveDay(samedi, 1m, "0410"), new LeaveDay(ferie, 1m, "0410") }
        };

        var corrections = new[]
        {
            Corr(1, "001", dimanche, am: true, pm: true, intitule: "Absence WE")
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            corrections,
            conges,
            new HashSet<DateTime> { ferie },
            new DateTime(2025, 11, 8),
            new DateTime(2025, 11, 12),
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_JourFutur_EstExclu()
    {
        // 2025-11-20 (Today) et 2025-11-21 (futur)
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[]
        {
            Plan(1, 1, Today, TimeSpan.FromHours(8), TimeSpan.FromHours(16)),
            Plan(2, 1, Today.AddDays(1), TimeSpan.FromHours(8), TimeSpan.FromHours(16))
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            Today,
            Today.AddDays(5),
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_JourPlanifieApresUnAutrePointage_ResteUneNonJustifiee()
    {
        // L'ancien garde-fou « dernier jour traité » est supprimé : un jour planifié sans pointage
        // est une absence, même si un autre jour de la période a été pointé (règle du dashboard).
        var j1 = new DateTime(2025, 11, 12);
        var j2 = new DateTime(2025, 11, 14);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[]
        {
            Plan(1, 1, j1, TimeSpan.FromHours(8), TimeSpan.FromHours(16)),
            Plan(2, 1, j2, TimeSpan.FromHours(8), TimeSpan.FromHours(16))
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            new[] { Corr(1, "001", j1) }, // Traité le 12/11
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            j1,
            j2,
            Today);

        // j1 a une correction donc pas non justifiée ; j2 n'a aucun pointage -> non justifiée.
        var row = Assert.Single(result);
        Assert.Equal(j2, row.Date);
        Assert.Equal("Non justifiée", row.Type);
        Assert.Equal(8m, row.DureeHeures);
    }

    [Fact]
    public void Calculer_CongeMalgrePointageSurUnAutreJour_EstConserve()
    {
        // Congé le 14/11/2025 (vendredi), pointage sur un autre jour : le congé reste affiché.
        var jConge = new DateTime(2025, 11, 14);
        var cartes = new[] { Carte(1, "001") };
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["1"] = new() { new LeaveDay(jConge, 1m, "0410") }
        };

        var result = AbsenceReportRules.Calculer(
            Array.Empty<T_PLANNING_SAL>(),
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            new DateTime(2025, 11, 10),
            new DateTime(2025, 11, 18),
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Congé SAGE", row.Type);
        Assert.Equal("0410", row.Motif);
        Assert.Equal(jConge, row.Date);
        Assert.Null(row.DureeHeures); // Sans planning -> durée null
    }

    [Fact]
    public void Calculer_CongeSage_GenereLigneCongeAvecMotif_PasDeNonJustifiee()
    {
        var date = new DateTime(2025, 11, 5);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(17), TimeSpan.FromHours(1)) };
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["1"] = new() { new LeaveDay(date, 1m, "CP") }
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Congé SAGE", row.Type);
        Assert.Equal("CP", row.Motif);
        Assert.Equal("Journée", row.Periode);
        Assert.Equal(8m, row.DureeHeures);
    }

    [Fact]
    public void Calculer_CongeDemiJournee_GenerePeriodeDemiJourneeEtDureeCalculee()
    {
        var date = new DateTime(2025, 11, 5);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(16)) }; // 8h
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["1"] = new() { new LeaveDay(date, 0.5m, "RTT") }
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Congé SAGE", row.Type);
        Assert.Equal("RTT", row.Motif);
        Assert.Equal("Demi-journée", row.Periode);
        Assert.Equal(4m, row.DureeHeures); // 8h * 0.5
    }

    [Fact]
    public void Calculer_DeuxDemiJourneesMemeJour_SommeDesValeursPlafonneeA1_DevientJournee()
    {
        // C12 : Deux demi-journées le même jour (ex: RTT matin + CP après-midi)
        var date = new DateTime(2025, 11, 5);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(16)) }; // 8h
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["1"] = new() { new LeaveDay(date, 0.5m, "RTT"), new LeaveDay(date, 0.5m, "CP") }
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Congé SAGE", row.Type);
        Assert.Equal("RTT, CP", row.Motif);
        Assert.Equal("Journée", row.Periode);
        Assert.Equal(8m, row.DureeHeures);
    }

    [Fact]
    public void Calculer_CongeSansPlanning_ConserveLigneAvecDureeVide()
    {
        // C12 : Congé posé sur un jour sans ligne de planning
        var date = new DateTime(2025, 11, 5);
        var cartes = new[] { Carte(1, "001") };
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["1"] = new() { new LeaveDay(date, 1m, "MALADIE") }
        };

        var result = AbsenceReportRules.Calculer(
            Array.Empty<T_PLANNING_SAL>(),
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Congé SAGE", row.Type);
        Assert.Equal("MALADIE", row.Motif);
        Assert.Equal("Journée", row.Periode);
        Assert.Null(row.DureeHeures);
    }

    [Theory]
    [InlineData(true, false, "Matin", 4.0)]
    [InlineData(false, true, "Après-midi", 4.0)]
    [InlineData(true, true, "Journée", 8.0)]
    public void Calculer_CorrectionArbitree_PeriodesEtDurees(bool am, bool pm, string expectedPeriode, decimal expectedDuree)
    {
        // C4 : Périodes et durées pour absence arbitrée
        var date = new DateTime(2025, 11, 6);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(16)) }; // 8h
        var corrections = new[] { Corr(1, "001", date, am: am, pm: pm, intitule: "Autorisation absence") };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            corrections,
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Arbitrée GTA", row.Type);
        Assert.Equal("Autorisation absence", row.Motif);
        Assert.Equal(expectedPeriode, row.Periode);
        Assert.Equal(expectedDuree, row.DureeHeures);
    }

    [Fact]
    public void Calculer_PrioriteRecoupement_CongePuisArbitreePuisNonJustifiee()
    {
        // R7 : Congé SAGE > Arbitrée GTA > Non justifiée sur le même jour
        var date = new DateTime(2025, 11, 7);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(16)) };
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["1"] = new() { new LeaveDay(date, 1m, "CP") }
        };
        var corrections = new[] { Corr(1, "001", date, am: true, pm: true, intitule: "Absence saisie") };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            corrections,
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Congé SAGE", row.Type);
        Assert.Equal("CP", row.Motif);
    }

    [Fact]
    public void Calculer_DeuxCartesMemeMatricule_PlanningsCumulesEtPrioriteDeterministePlusPetitId()
    {
        // C11 : Deux cartes avec même clé matricule "00369" et "369".
        // Carte 1 ID=10, Carte 2 ID=20.
        // Planning 1 ID=100 (sur IDSAL=10, 8h-16h) vs Planning 2 ID=50 (sur IDSAL=20, 8h-17h, 9h)
        // Le plus petit ID de planning (ID=50) doit être retenu déterministement.
        var date = new DateTime(2025, 11, 4);
        var cartes = new[]
        {
            Carte(10, "00369", "Rasoa", "Mireille"),
            Carte(20, "369", "Rasoa", "Mireille")
        };

        var plannings = new[]
        {
            Plan(100, 10, date, TimeSpan.FromHours(8), TimeSpan.FromHours(16)), // 8h
            Plan(50, 20, date, TimeSpan.FromHours(8), TimeSpan.FromHours(17))   // 9h (plus petit ID = 50)
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal(9m, row.DureeHeures); // issu du planning ID=50
    }

    [Fact]
    public void Calculer_FiltreMatricule_ZerosDeTeteEtEspaces_RapprocheCorrectement()
    {
        // Salarié " 00369 " dans cartes et congé pour "369"
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, " 00369 ") };
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["369"] = new() { new LeaveDay(date, 1m, "RECUP") }
        };

        var result = AbsenceReportRules.Calculer(
            Array.Empty<T_PLANNING_SAL>(),
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("00369", row.Matricule);
        Assert.Equal("RECUP", row.Motif);
    }

    [Fact]
    public void Calculer_CarteAvecEspacesDeFin_AfficheValeursSansEspaces()
    {
        // Carte avec NCHAR(10) espaces de fin sur matricule, nom, prénom, département
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "03919     ", nom: "ANDRIANARISOA   ", prenom: "Jean Eddy   ", dep: "BR730   ") };
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["3919"] = new() { new LeaveDay(date, 1m, "0410") }
        };

        var result = AbsenceReportRules.Calculer(
            Array.Empty<T_PLANNING_SAL>(),
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("03919", row.Matricule);
        Assert.Equal("ANDRIANARISOA", row.Nom);
        Assert.Equal("Jean Eddy", row.Prenom);
        Assert.Equal("BR730", row.Departement);
    }

    [Fact]
    public void Calculer_HaEgaleHd_ShiftPause_NeGenereAucuneLigne()
    {
        // C7 & R10 : Shift 17 / Pause avec HA == HD = 00:00 -> jour de repos, aucune absence.
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, TimeSpan.Zero, TimeSpan.Zero) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_ShiftDeNuit_TraverseMinuit_DureeCalculeeCorrectement()
    {
        // De 22:00 à 06:00 (8h) avec 0.5h de pause -> 7.5h
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, TimeSpan.FromHours(22), TimeSpan.FromHours(6), TimeSpan.FromMinutes(30)) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal(7.5m, row.DureeHeures);
    }

    [Fact]
    public void Calculer_JourPlanifieSansAucuneCorrectionSurLaPeriode_GenereNonJustifiee()
    {
        // Le dashboard ne dépend pas d'un « dernier jour traité » : un jour planifié avec heures et
        // sans aucun pointage est une absence non justifiée.
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(17)) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Non justifiée", row.Type);
        Assert.Equal(9m, row.DureeHeures);
    }

    [Fact]
    public void Calculer_SalarieAbsentDesCartes_EstIgnore()
    {
        // Planning et congés pour le salarié IDSAL=99, mais cartes ne contient que ID=1
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 99, date, TimeSpan.FromHours(8), TimeSpan.FromHours(17)) };
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["99"] = new() { new LeaveDay(date, 1m, "CP") }
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_DateDeFinInclusive_PrendEnCompteDernierJour()
    {
        // Période du 03/11 au 05/11. Plannings les 3 jours. Tous doivent être présents.
        var j1 = new DateTime(2025, 11, 3);
        var j2 = new DateTime(2025, 11, 4);
        var j3 = new DateTime(2025, 11, 5);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[]
        {
            Plan(1, 1, j1, TimeSpan.FromHours(8), TimeSpan.FromHours(16)),
            Plan(2, 1, j2, TimeSpan.FromHours(8), TimeSpan.FromHours(16)),
            Plan(3, 1, j3, TimeSpan.FromHours(8), TimeSpan.FromHours(16))
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            j1,
            j3,
            Today);

        Assert.Equal(3, result.Count);
        Assert.Equal(j1, result[0].Date);
        Assert.Equal(j2, result[1].Date);
        Assert.Equal(j3, result[2].Date);
    }

    [Fact]
    public void Calculer_DeuxSalariesDontUnSeulFiltre_SeulLeSalarieEnCarteEstCalcule()
    {
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") }; // Seul 001 est dans les cartes filtrées
        var plannings = new[]
        {
            Plan(1, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(16)),
            Plan(2, 2, date, TimeSpan.FromHours(8), TimeSpan.FromHours(16)) // pour IDSAL 2 absent des cartes
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("001", row.Matricule);
    }

    private static T_PLANNING_SAL PlanWithShift(int id, int idsal, DateTime date, int noShift, TimeSpan? ha = null, TimeSpan? hd = null, TimeSpan? pause = null, bool offP = false)
        => new()
        {
            ID = id,
            IDSAL = idsal,
            NoSHIFT = noShift,
            DateP = date,
            HA = ha.HasValue ? date.Date.Add(ha.Value) : null,
            HD = hd.HasValue ? date.Date.Add(hd.Value) : null,
            Pause = pause,
            OffP = offP
        };

    [Fact]
    public void Calculer_ShiftSansHeures_AucunPointage_NeGenereAucuneLigne()
    {
        // (a) Shift 17 (Pause) sans heures et aucun pointage -> jour de repos, aucune ligne
        var date = new DateTime(2025, 11, 4); // Mardi ouvré
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { PlanWithShift(1, 1, date, 17, TimeSpan.Zero, TimeSpan.Zero) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_ShiftSansHeures_CongeSageCeJour_LigneCongeSageConservee()
    {
        // (b) Shift 17 (Pause) avec congé SAGE -> la priorité 1 Congé SAGE prime et la ligne est conservée
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { PlanWithShift(1, 1, date, 17, TimeSpan.Zero, TimeSpan.Zero) };
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["1"] = new() { new LeaveDay(date, 1m, "0410") }
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Congé SAGE", row.Type);
        Assert.Equal("0410", row.Motif);
        Assert.Null(row.DureeHeures);
    }

    [Fact]
    public void Calculer_ShiftNormal_AucunPointage_GenereNonJustifieeAvecDuree()
    {
        // (c) Shift 13 normal (06:00 à 18:00) avec aucun pointage -> Non justifiée avec 12h
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { PlanWithShift(1, 1, date, 13, TimeSpan.FromHours(6), TimeSpan.FromHours(18)) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Non justifiée", row.Type);
        Assert.Equal(12m, row.DureeHeures);
    }

    [Fact]
    public void Calculer_ShiftIntrouvable_SansHeures_NeGenereAucuneLigne()
    {
        // (d) Shift 999 absent du catalogue et ligne de planning sans heures : rien à valoriser,
        // ce n'est pas une absence (la référence est constituée des heures du planning).
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { PlanWithShift(1, 1, date, 999) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_ShiftSansHeures_AvecPointage_NeGenereAucuneLigne()
    {
        // (e) Salarié qui a pointé un jour de Pause -> pas d'absence
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { PlanWithShift(1, 1, date, 17, TimeSpan.Zero, TimeSpan.Zero) };
        var corrections = new[] { Corr(1, "001", date) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            corrections,
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_SalariéAlternantPauseEtTravail_SeulLeJourDeTravailEstCompteEnNonJustifiee()
    {
        // (f) Jour 1 : Shift 13 (travail) non pointé -> Non justifiée
        //     Jour 2 : Shift 17 (Pause) non pointé -> pas d'absence (repos)
        var jTravail = new DateTime(2025, 11, 4);
        var jPause = new DateTime(2025, 11, 5);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[]
        {
            PlanWithShift(1, 1, jTravail, 13, TimeSpan.FromHours(6), TimeSpan.FromHours(18)),
            PlanWithShift(2, 1, jPause, 17, TimeSpan.Zero, TimeSpan.Zero)
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            jTravail,
            jPause,
            Today);

        var row = Assert.Single(result);
        Assert.Equal(jTravail, row.Date);
        Assert.Equal("Non justifiée", row.Type);
        Assert.Equal(12m, row.DureeHeures);
    }

    [Fact]
    public void Calculer_JourOff_AvecCongeSage_NeGenereAucuneLigne()
    {
        // Un jour de repos planifié n'est jamais une absence, même couvert par un congé SAGE.
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, offP: true) };
        var conges = new Dictionary<string, List<LeaveDay>>
        {
            ["1"] = new() { new LeaveDay(date, 1m, "0410") }
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            conges,
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_JourOff_AvecCorrectionAbsence_NeGenereAucuneLigne()
    {
        // Un jour de repos planifié n'est pas une absence, même si une correction le mentionne.
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, offP: true) };
        var corrections = new[] { Corr(1, "001", date, am: true, pm: true, intitule: "Repos décalé") };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            corrections,
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_JourOff_AvecHeuresResiduelles_NeGenereAucuneLigne()
    {
        // Des données historiques peuvent porter des heures sur une ligne OFF : le drapeau prime.
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[] { Plan(1, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(16), offP: true) };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculer_PlanningOffEtTravailMemeJour_LeJourTravaillePrime()
    {
        // Deux lignes de planning le même jour : une OFF (plus petit ID) et une travaillée.
        var date = new DateTime(2025, 11, 4);
        var cartes = new[] { Carte(1, "001") };
        var plannings = new[]
        {
            Plan(1, 1, date, offP: true),
            Plan(2, 1, date, TimeSpan.FromHours(8), TimeSpan.FromHours(16))
        };

        var result = AbsenceReportRules.Calculer(
            plannings,
            cartes,
            Array.Empty<T_HEURECORRIGER>(),
            new Dictionary<string, List<LeaveDay>>(),
            new HashSet<DateTime>(),
            date,
            date,
            Today);

        var row = Assert.Single(result);
        Assert.Equal("Non justifiée", row.Type);
        Assert.Equal(8m, row.DureeHeures);
    }
}
