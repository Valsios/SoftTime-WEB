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
        Assert.Single(contexte.Pointages);
    }
}
