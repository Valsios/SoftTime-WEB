using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class TimekeepingImportTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public FakeExternalSourceService External { get; } = new();
        public TimekeepingService Service { get; }

        public Contexte()
        {
            Uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Get<T_BDD_POINTEUSE>().Items.Add(new T_BDD_POINTEUSE { ID = 1, NOM_BD = "PTE_TEST", ACTIVE = true });
            Uow.Get<T_CARDPAIE>().Items.Add(new T_CARDPAIE
            {
                ID = 1,
                SAGE_MATRICULE = "001",
                SAGE_BDD = "SAGE_TEST",
                POINTEUSE_BDD = "PTE_TEST",
                POINTEUSE_NUMERO = "B1"
            });
            Uow.Get<T_CLOCK>().Items.Add(new T_CLOCK { MULTIPOINT = true });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser());
            Service = new TimekeepingService(Uow, tenant, External, new HistoryArchiveService(Uow, tenant));
        }

        public List<T_POINTAGE> Pointages => Uow.Get<T_POINTAGE>().Items;
    }

    [Fact]
    public async Task ImportPunchesExcel_DedoublonneLesLignesIdentiquesDuFichier()
    {
        var contexte = new Contexte();
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["Date"] = "2026-08-31", ["Heure"] = "08:00" },
            new() { ["Matricule"] = "001", ["Date"] = "2026-08-31", ["Heure"] = "08:00" }
        };

        var resultat = await contexte.Service.ImportPunchesExcelAsync(rows);

        Assert.Equal(1, resultat.Imported);
        Assert.Equal(1, resultat.Skipped);
        Assert.Equal("1 pointage(s) importé(s), 1 ligne(s) ignorée(s).", resultat.Message);
        Assert.Single(contexte.Pointages);
    }

    [Fact]
    public async Task ImportPunchesExcel_DedoublonneContreLaBase()
    {
        var contexte = new Contexte();
        contexte.Pointages.Add(new T_POINTAGE
        {
            NOM_BDD_SAGE = "SAGE_TEST",
            NOM_BDD_POINTEUSE = "PTE_TEST",
            MATRICULE_SAGE = "001",
            DATE_POINTAGE = Lundi.Date,
            HEURE_POINTAGE = new TimeSpan(8, 0, 0)
        });
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["Date"] = "2026-08-31", ["Heure"] = "08:00" }
        };

        var resultat = await contexte.Service.ImportPunchesExcelAsync(rows);

        Assert.Equal(0, resultat.Imported);
        Assert.Equal(1, resultat.Skipped);
        Assert.Single(contexte.Pointages);
    }

    [Fact]
    public async Task ImportFromClock_DeuxPasses_DedoublonneEnMultiPointage()
    {
        var contexte = new Contexte();
        contexte.External.Users.Add(new ExternalPunchUser(1, "B1", "001", "Test"));
        contexte.External.Punches.Add(new ExternalPunch(1, Lundi.AddHours(8), "IO"));
        var requete = new ImportPunchesRequest(null, null, Lundi, Lundi);

        var premier = await contexte.Service.ImportFromClockAsync(requete);
        var second = await contexte.Service.ImportFromClockAsync(requete);

        Assert.Equal(1, premier.Imported);
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
        Assert.Equal("1 pointage(s) importé(s), 0 ignoré(s).", premier.Message);
        Assert.Equal("0 pointage(s) importé(s), 1 ignoré(s).", second.Message);
        Assert.Single(contexte.Pointages);
    }

    // ------------------------------------------------------------------ semaine validée / arbitrage

    private static void ValiderSemaine(Contexte contexte, DateTime lundi)
        => contexte.Uow.Get<T_HS_VALIDER>().Items.Add(new T_HS_VALIDER
        {
            BDD_SAGE = "SAGE_TEST",
            MATRICULES = "001",
            DATE_DEB = lundi,
            DATE_FIN = lundi.AddDays(6),
            VALIDATION_HS = true
        });

    private static T_HEUREANOMALIE AnomalieAbsence(Contexte contexte, DateTime jour)
    {
        var anomalie = new T_HEUREANOMALIE
        {
            ID = 1,
            BDD_SAGE = "SAGE_TEST",
            BDD_POINTEUSE = "PTE_TEST",
            MATRICULE_SAGE = "001",
            DATE_POINTAGE_IN = jour,
            DATE_POINTAGE_OUT = jour,
            ABS_AM = true,
            ABS_PM = true
        };
        contexte.Uow.Get<T_HEUREANOMALIE>().Items.Add(anomalie);
        return anomalie;
    }

    [Fact]
    public async Task Corrections_SemaineValidee_SaisieSuppressionEtImportRefuses()
    {
        var contexte = new Contexte();
        ValiderSemaine(contexte, Lundi);
        var existante = new T_HEURECORRIGER
        {
            ID = 5,
            BDD_SAGE = "SAGE_TEST",
            MATRICULE_SAGE = "001",
            DATE_POINTAGE_IN = Lundi,
            HENTREE = new TimeSpan(8, 0, 0),
            HSORTIE = new TimeSpan(17, 0, 0)
        };
        contexte.Uow.Get<T_HEURECORRIGER>().Items.Add(existante);

        await Assert.ThrowsAsync<InvalidOperationException>(() => contexte.Service.SaveCorrectionAsync(
            new CorrectedHourDto { Matricule = "001", DateIn = Lundi.AddDays(1), HEntree = new TimeSpan(8, 0, 0), HSortie = new TimeSpan(20, 0, 0) }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => contexte.Service.DeleteCorrectionAsync(5));

        var import = await contexte.Service.ImportCorrectionsExcelAsync(new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["DateEntree"] = "2026-09-01", ["HeureEntree"] = "08:00", ["HeureSortie"] = "20:00" }
        });

        Assert.Equal(0, import.Imported);
        Assert.Equal(1, import.Skipped);
        Assert.Same(existante, Assert.Single(contexte.Uow.Get<T_HEURECORRIGER>().Items));
    }

    [Fact]
    public async Task ValidationAnomalie_AbsenceJourneeComplete_SansHeures_DevientUneAbsence()
    {
        var contexte = new Contexte();
        var anomalie = AnomalieAbsence(contexte, Lundi);

        await contexte.Service.ValidateAnomaliesAsync(new[] { anomalie.ID });

        var correction = Assert.Single(contexte.Uow.Get<T_HEURECORRIGER>().Items);
        Assert.True(correction.ABSENCE_AM);
        Assert.True(correction.ABSENCE_PM);
        Assert.Null(correction.HENTREE);
        Assert.Null(correction.HSORTIE);
        Assert.Empty(contexte.Uow.Get<T_HEUREANOMALIE>().Items);
    }

    [Fact]
    public async Task ValidationAnomalie_SemaineValidee_EstRefusee()
    {
        var contexte = new Contexte();
        ValiderSemaine(contexte, Lundi);
        var anomalie = AnomalieAbsence(contexte, Lundi.AddDays(2));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => contexte.Service.ValidateAnomaliesAsync(new[] { anomalie.ID }));

        Assert.Empty(contexte.Uow.Get<T_HEURECORRIGER>().Items);
    }
}
