using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

public class PlanningServiceTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public PlanningService Service { get; }
        public List<PlanningSalarie> Planning => Uow.Get<PlanningSalarie>().Items;

        public Contexte()
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Get<CardPaie>().Items.Add(new CardPaie
            {
                Id = 1,
                SageMatricule = "001",
                SageBdd = "SAGE_TEST",
                PointeuseBdd = "PTE_TEST"
            });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
            Service = new PlanningService(Uow, tenant, new FakePlanningStatisticsReader());
        }

        public void Shift(int noShift, int haHeure, int haMinute, int hdHeure, int hdMinute, int? pauseHeure = 0)
        {
            Uow.Get<Planning>().Items.Add(new Planning
            {
                NoShift = noShift,
                Ha = new TimeSpan(haHeure, haMinute, 0),
                Hd = new TimeSpan(hdHeure, hdMinute, 0),
                Pause = pauseHeure.HasValue ? new TimeSpan(pauseHeure.Value, 0, 0) : null,
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
        Assert.Equal(Lundi.AddHours(17), ligne.Ha);
        Assert.Equal(Lundi.AddDays(1).AddHours(7), ligne.Hd);
        Assert.Equal(TimeSpan.Zero, ligne.Pause);
        Assert.False(ligne.OffP);
        Assert.Equal(1, ligne.NoShift);
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
        Assert.Equal(Lundi.AddHours(7).AddMinutes(30), ligne.Ha);
        Assert.Equal(Lundi.AddHours(17), ligne.Hd);
    }

    [Fact]
    public async Task SaveEmployeePlanning_LigneSansShift_ConserveHeuresExistantes()
    {
        var contexte = new Contexte();
        contexte.Uow.Get<PlanningSalarie>().Items.Add(new PlanningSalarie
        {
            Id = 10,
            IdSal = 1,
            DateP = Lundi.Date,
            Ha = Lundi.AddHours(8),
            Hd = Lundi.AddHours(17),
            Pause = TimeSpan.Zero,
            OffP = false,
            BddSage = "SAGE_TEST",
            BddPointeuse = "PTE_TEST"
        });

        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(10, 1, null, Lundi, null, null, null, false)
        });

        var ligne = Assert.Single(contexte.Planning);
        Assert.Equal(Lundi.AddHours(8), ligne.Ha);
        Assert.Equal(Lundi.AddHours(17), ligne.Hd);
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
        Assert.Equal(Lundi.AddHours(22), ligne.Ha);
        Assert.Equal(Lundi.AddDays(1).AddHours(6), ligne.Hd);
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
        Assert.Null(ligne.Ha);
        Assert.Null(ligne.Hd);
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

        await Assert.ThrowsAsync<DomainException>(() =>
            contexte.Service.SaveEmployeePlanningAsync(new[]
            {
                new EmployeePlanningDto(0, 1, 99, Lundi, null, null, null, false)
            }));
    }

    [Fact]
    public async Task SaveEmployeePlanning_SansHeures_Rejette()
    {
        var contexte = new Contexte();

        await Assert.ThrowsAsync<DomainException>(() =>
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
        Assert.Equal(Lundi.AddHours(17), ligne.Ha);
        Assert.Equal(Lundi.AddDays(1).AddHours(7), ligne.Hd);
        Assert.Equal(1, ligne.NoShift);
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
        Assert.Null(ligne.Ha);
        Assert.Null(ligne.Hd);
        Assert.Null(ligne.NoShift);
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
        Assert.Equal(Lundi.AddHours(22), ligne.Ha);
        Assert.Equal(Lundi.AddDays(1).AddHours(6), ligne.Hd);
        Assert.Equal(TimeSpan.Zero, ligne.Pause);
        Assert.Null(ligne.NoShift);
    }
}
