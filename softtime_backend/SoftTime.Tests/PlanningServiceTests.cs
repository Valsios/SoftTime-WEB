using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class PlanningServiceTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public PlanningService Service { get; }
        public List<T_PLANNING_SAL> Planning => Uow.Get<T_PLANNING_SAL>().Items;

        public Contexte()
        {
            Uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Get<T_CARDPAIE>().Items.Add(new T_CARDPAIE
            {
                ID = 1,
                SAGE_MATRICULE = "001",
                SAGE_BDD = "SAGE_TEST",
                POINTEUSE_BDD = "PTE_TEST"
            });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser());
            Service = new PlanningService(Uow, tenant, new FakePlanningStatisticsReader());
        }

        public void Shift(int noShift, int haHeure, int haMinute, int hdHeure, int hdMinute, int? pauseHeure = 0)
        {
            Uow.Get<T_PLANNING>().Items.Add(new T_PLANNING
            {
                NoSHIFT = noShift,
                HA = new TimeSpan(haHeure, haMinute, 0),
                HD = new TimeSpan(hdHeure, hdMinute, 0),
                PAUSE = pauseHeure.HasValue ? new TimeSpan(pauseHeure.Value, 0, 0) : null,
                Intitule = $"Shift {noShift}"
            });
        }
    }

    [Fact]
    public async Task SaveEmployeePlanning_ShiftNuit_EcritHDLeLendemain()
    {
        var contexte = new Contexte();
        contexte.Shift(1, 17, 0, 7, 0);

        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });

        var ligne = Assert.Single(contexte.Planning);
        Assert.Equal(Lundi.Date, ligne.DateP);
        Assert.Equal(Lundi.AddHours(17), ligne.HA);
        Assert.Equal(Lundi.AddDays(1).AddHours(7), ligne.HD);
        Assert.Equal(TimeSpan.Zero, ligne.Pause);
        Assert.False(ligne.OffP);
        Assert.Equal(1, ligne.NoSHIFT);
    }

    [Fact]
    public async Task SaveEmployeePlanning_ShiftJour_EcritMemeJour()
    {
        var contexte = new Contexte();
        contexte.Shift(1, 7, 30, 17, 0);

        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });

        var ligne = Assert.Single(contexte.Planning);
        Assert.Equal(Lundi.AddHours(7).AddMinutes(30), ligne.HA);
        Assert.Equal(Lundi.AddHours(17), ligne.HD);
    }

    [Fact]
    public async Task SaveEmployeePlanning_LigneSansShift_ConserveHeuresExistantes()
    {
        var contexte = new Contexte();
        contexte.Uow.Get<T_PLANNING_SAL>().Items.Add(new T_PLANNING_SAL
        {
            ID = 10,
            IDSAL = 1,
            DateP = Lundi.Date,
            HA = Lundi.AddHours(8),
            HD = Lundi.AddHours(17),
            Pause = TimeSpan.Zero,
            OffP = false,
            BDD_SAGE = "SAGE_TEST",
            BDD_POINTEUSE = "PTE_TEST"
        });

        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(10, 1, null, Lundi, null, null, null, false)
        });

        var ligne = Assert.Single(contexte.Planning);
        Assert.Equal(Lundi.AddHours(8), ligne.HA);
        Assert.Equal(Lundi.AddHours(17), ligne.HD);
    }

    [Fact]
    public async Task SaveEmployeePlanning_HeuresPersonnaliseesNuit()
    {
        var contexte = new Contexte();

        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, null, Lundi, Lundi.AddHours(22), null, Lundi.AddHours(6), false)
        });

        var ligne = Assert.Single(contexte.Planning);
        Assert.Equal(Lundi.AddHours(22), ligne.HA);
        Assert.Equal(Lundi.AddDays(1).AddHours(6), ligne.HD);
    }

    [Fact]
    public async Task SaveEmployeePlanning_Off_EcritHeuresNullEtPauseZero()
    {
        var contexte = new Contexte();
        contexte.Shift(1, 17, 0, 7, 0);

        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, true)
        });

        var ligne = Assert.Single(contexte.Planning);
        Assert.Null(ligne.HA);
        Assert.Null(ligne.HD);
        Assert.Equal(TimeSpan.Zero, ligne.Pause);
        Assert.True(ligne.OffP);
    }

    [Fact]
    public async Task SaveEmployeePlanning_DeuxFois_MetAJourSansDoublon()
    {
        var contexte = new Contexte();
        contexte.Shift(1, 17, 0, 7, 0);

        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });
        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });

        Assert.Single(contexte.Planning);
    }

    [Fact]
    public async Task SaveEmployeePlanning_ShiftInconnu_Rejette()
    {
        var contexte = new Contexte();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexte.Service.SaveEmployeePlanningAsync(new[]
            {
                new EmployeePlanningDto(0, 1, 99, Lundi, null, null, null, false)
            }));
    }

    [Fact]
    public async Task SaveEmployeePlanning_SansHeures_Rejette()
    {
        var contexte = new Contexte();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            contexte.Service.SaveEmployeePlanningAsync(new[]
            {
                new EmployeePlanningDto(0, 1, null, Lundi, null, null, null, false)
            }));
    }

    [Fact]
    public async Task SaveEmployeePlanning_PauseNull_DevientZero()
    {
        var contexte = new Contexte();
        contexte.Shift(1, 8, 0, 17, 0, pauseHeure: null);

        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });

        Assert.Equal(TimeSpan.Zero, Assert.Single(contexte.Planning).Pause);
    }

    [Fact]
    public async Task ImportExcel_ShiftNuit()
    {
        var contexte = new Contexte();
        contexte.Shift(1, 17, 0, 7, 0);
        var headers = new[] { "Matricule", "2026-08-31" };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["2026-08-31"] = "1" }
        };

        var resultat = await contexte.Service.ImportExcelAsync(rows, headers);

        Assert.Equal(1, resultat.Imported);
        var ligne = Assert.Single(contexte.Planning);
        Assert.Equal(Lundi.AddHours(17), ligne.HA);
        Assert.Equal(Lundi.AddDays(1).AddHours(7), ligne.HD);
        Assert.Equal(1, ligne.NoSHIFT);
        Assert.Equal(TimeSpan.Zero, ligne.Pause);
    }

    [Fact]
    public async Task ImportExcel_Off()
    {
        var contexte = new Contexte();
        var headers = new[] { "Matricule", "2026-08-31" };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["2026-08-31"] = "OFF" }
        };

        await contexte.Service.ImportExcelAsync(rows, headers);

        var ligne = Assert.Single(contexte.Planning);
        Assert.True(ligne.OffP);
        Assert.Null(ligne.HA);
        Assert.Null(ligne.HD);
        Assert.Null(ligne.NoSHIFT);
        Assert.Equal(TimeSpan.Zero, ligne.Pause);
    }

    [Fact]
    public async Task ImportExcel_CelluleVide_Ignore()
    {
        var contexte = new Contexte();
        var headers = new[] { "Matricule", "2026-08-31" };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["2026-08-31"] = "" }
        };

        var resultat = await contexte.Service.ImportExcelAsync(rows, headers);

        Assert.Equal(0, resultat.Imported);
        Assert.Empty(contexte.Planning);
    }

    [Fact]
    public async Task GenerateFromMatriculeCsv_Nuit_EcritHDLeLendemain()
    {
        var contexte = new Contexte();
        var lignes = new[]
        {
            new[] { "Matricule", "2026-08-31", "22:00", "00:00", "06:00" },
            new[] { "001", "2026-08-31", "22:00", "00:00", "06:00" }
        };

        var resultat = await contexte.Service.GenerateFromMatriculeCsvAsync(lignes);

        Assert.Equal(1, resultat.Imported);
        var ligne = Assert.Single(contexte.Planning);
        Assert.Equal(Lundi.AddHours(22), ligne.HA);
        Assert.Equal(Lundi.AddDays(1).AddHours(6), ligne.HD);
        Assert.Equal(TimeSpan.Zero, ligne.Pause);
        Assert.Null(ligne.NoSHIFT);
    }
}
