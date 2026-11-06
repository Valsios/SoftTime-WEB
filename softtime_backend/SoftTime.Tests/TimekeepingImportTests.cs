using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

public class TimekeepingImportTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public FakeExternalSourceService External { get; } = new();
        public TimekeepingServices Service { get; }

        public Contexte()
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 1, NomBd = "PTE_TEST", Active = true });
            Uow.Get<CardPaie>().Items.Add(new CardPaie
            {
                Id = 1,
                SageMatricule = "001",
                SageBdd = "SAGE_TEST",
                PointeuseBdd = "PTE_TEST",
                PointeuseNumero = "B1"
            });
            Uow.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { MultiPoint = true });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
            Service = new TimekeepingServices(Uow, tenant, External, new HistoryArchiveService(Uow));
        }

        public List<Pointage> Pointages => Uow.Get<Pointage>().Items;
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
        contexte.Pointages.Add(new Pointage
        {
            NomBddSage = "SAGE_TEST",
            NomBddPointeuse = "PTE_TEST",
            MatriculeSage = "001",
            DatePointage = Lundi.Date,
            HeurePointage = new TimeSpan(8, 0, 0)
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
        => contexte.Uow.Get<HsValidee>().Items.Add(new HsValidee
        {
            BddSage = "SAGE_TEST",
            Matricules = "001",
            DateDeb = lundi,
            DateFin = lundi.AddDays(6),
            ValidationHs = true
        });

    private static HeureAnomalie AnomalieAbsence(Contexte contexte, DateTime jour)
    {
        var anomalie = new HeureAnomalie
        {
            Id = 1,
            BddSage = "SAGE_TEST",
            BddPointeuse = "PTE_TEST",
            MatriculeSage = "001",
            DatePointageIn = jour,
            DatePointageOut = jour,
            AbsAm = true,
            AbsPm = true
        };
        contexte.Uow.Get<HeureAnomalie>().Items.Add(anomalie);
        return anomalie;
    }

    [Fact]
    public async Task Corrections_SemaineValidee_SaisieSuppressionEtImportRefuses()
    {
        var contexte = new Contexte();
        ValiderSemaine(contexte, Lundi);
        var existante = new HeureCorrigee
        {
            Id = 5,
            BddSage = "SAGE_TEST",
            MatriculeSage = "001",
            DatePointageIn = Lundi,
            HEntree = new TimeSpan(8, 0, 0),
            HSortie = new TimeSpan(17, 0, 0)
        };
        contexte.Uow.Get<HeureCorrigee>().Items.Add(existante);

        await Assert.ThrowsAsync<DomainException>(() => contexte.Service.SaveCorrectionAsync(
            new CorrectedHourDto { Matricule = "001", DateIn = Lundi.AddDays(1), HEntree = new TimeSpan(8, 0, 0), HSortie = new TimeSpan(20, 0, 0) }));
        await Assert.ThrowsAsync<DomainException>(() => contexte.Service.DeleteCorrectionAsync(5));

        var import = await contexte.Service.ImportCorrectionsExcelAsync(new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["DateEntree"] = "2026-09-01", ["HeureEntree"] = "08:00", ["HeureSortie"] = "20:00" }
        });

        Assert.Equal(0, import.Imported);
        Assert.Equal(1, import.Skipped);
        Assert.Same(existante, Assert.Single(contexte.Uow.Get<HeureCorrigee>().Items));
    }

    [Fact]
    public async Task ValidationAnomalie_AbsenceJourneeComplete_SansHeures_DevientUneAbsence()
    {
        var contexte = new Contexte();
        var anomalie = AnomalieAbsence(contexte, Lundi);

        await contexte.Service.ValidateAnomaliesAsync(new[] { anomalie.Id });

        var correction = Assert.Single(contexte.Uow.Get<HeureCorrigee>().Items);
        Assert.True(correction.AbsenceAm);
        Assert.True(correction.AbsencePm);
        Assert.Null(correction.HEntree);
        Assert.Null(correction.HSortie);
        Assert.Empty(contexte.Uow.Get<HeureAnomalie>().Items);
    }

    [Fact]
    public async Task ValidationAnomalie_SemaineValidee_EstRefusee()
    {
        var contexte = new Contexte();
        ValiderSemaine(contexte, Lundi);
        var anomalie = AnomalieAbsence(contexte, Lundi.AddDays(2));

        await Assert.ThrowsAsync<DomainException>(
            () => contexte.Service.ValidateAnomaliesAsync(new[] { anomalie.Id }));

        Assert.Empty(contexte.Uow.Get<HeureCorrigee>().Items);
    }
}
