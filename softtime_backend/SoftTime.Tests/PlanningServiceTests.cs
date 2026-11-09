using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.RequestContext;

namespace SoftTime.Tests;

public class PlanningServiceTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public PlanningServices Service { get; }
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
            Service = new PlanningServices(Uow, tenant, new FakePlanningStatisticsReader());
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
        // Arrange
        var contexte = new Contexte();
        contexte.Shift(1, 17, 0, 7, 0);

        // Act
        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });

        // Assert
        var ligne = contexte.Planning.Should().ContainSingle().Which;
        ligne.DateP.Should().Be(Lundi.Date);
        ligne.Ha.Should().Be(Lundi.AddHours(17));
        ligne.Hd.Should().Be(Lundi.AddDays(1).AddHours(7));
        ligne.Pause.Should().Be(TimeSpan.Zero);
        ligne.OffP.Should().BeFalse();
        ligne.NoShift.Should().Be(1);
    }

    [Fact]
    public async Task SaveEmployeePlanning_ShiftJour_EcritMemeJour()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.Shift(1, 7, 30, 17, 0);

        // Act
        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });

        // Assert
        var ligne = contexte.Planning.Should().ContainSingle().Which;
        ligne.Ha.Should().Be(Lundi.AddHours(7).AddMinutes(30));
        ligne.Hd.Should().Be(Lundi.AddHours(17));
    }

    [Fact]
    public async Task SaveEmployeePlanning_LigneSansShift_ConserveHeuresExistantes()
    {
        // Arrange
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

        // Act
        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(10, 1, null, Lundi, null, null, null, false)
        });

        // Assert
        var ligne = contexte.Planning.Should().ContainSingle().Which;
        ligne.Ha.Should().Be(Lundi.AddHours(8));
        ligne.Hd.Should().Be(Lundi.AddHours(17));
    }

    [Fact]
    public async Task SaveEmployeePlanningAsync_HeuresPersonnaliseesNuit_EnregistreLaSortieLeLendemain()
    {
        // Arrange
        var contexte = new Contexte();

        // Act
        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, null, Lundi, Lundi.AddHours(22), null, Lundi.AddHours(6), false)
        });

        // Assert
        var ligne = contexte.Planning.Should().ContainSingle().Which;
        ligne.Ha.Should().Be(Lundi.AddHours(22));
        ligne.Hd.Should().Be(Lundi.AddDays(1).AddHours(6));
    }

    [Fact]
    public async Task SaveEmployeePlanning_Off_EcritHeuresNullEtPauseZero()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.Shift(1, 17, 0, 7, 0);

        // Act
        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, true)
        });

        // Assert
        var ligne = contexte.Planning.Should().ContainSingle().Which;
        ligne.Ha.Should().BeNull();
        ligne.Hd.Should().BeNull();
        ligne.Pause.Should().Be(TimeSpan.Zero);
        ligne.OffP.Should().BeTrue();
    }

    [Fact]
    public async Task SaveEmployeePlanning_DeuxFois_MetAJourSansDoublon()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.Shift(1, 17, 0, 7, 0);
        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });

        // Act
        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });

        // Assert
        contexte.Planning.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveEmployeePlanning_ShiftInconnu_Rejette()
    {
        // Arrange
        var contexte = new Contexte();

        // Act
        Func<Task> act = () =>
            contexte.Service.SaveEmployeePlanningAsync(new[]
            {
                new EmployeePlanningDto(0, 1, 99, Lundi, null, null, null, false)
            });

        // Assert
        await act.Should().ThrowExactlyAsync<DomainException>();
    }

    [Fact]
    public async Task SaveEmployeePlanning_SansHeures_Rejette()
    {
        // Arrange
        var contexte = new Contexte();

        // Act
        Func<Task> act = () =>
            contexte.Service.SaveEmployeePlanningAsync(new[]
            {
                new EmployeePlanningDto(0, 1, null, Lundi, null, null, null, false)
            });

        // Assert
        await act.Should().ThrowExactlyAsync<DomainException>();
    }

    [Fact]
    public async Task SaveEmployeePlanning_PauseNull_DevientZero()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.Shift(1, 8, 0, 17, 0, pauseHeure: null);

        // Act
        await contexte.Service.SaveEmployeePlanningAsync(new[]
        {
            new EmployeePlanningDto(0, 1, 1, Lundi, null, null, null, false)
        });

        // Assert
        contexte.Planning.Should().ContainSingle().Which.Pause.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task ImportExcelAsync_ShiftNuit_EnregistreLaSortieLeLendemain()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.Shift(1, 17, 0, 7, 0);
        var headers = new[] { "Matricule", "2026-08-31" };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["2026-08-31"] = "1" }
        };

        // Act
        var resultat = await contexte.Service.ImportExcelAsync(rows, headers);

        // Assert
        resultat.Imported.Should().Be(1);
        var ligne = contexte.Planning.Should().ContainSingle().Which;
        ligne.Ha.Should().Be(Lundi.AddHours(17));
        ligne.Hd.Should().Be(Lundi.AddDays(1).AddHours(7));
        ligne.NoShift.Should().Be(1);
        ligne.Pause.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task ImportExcelAsync_JourOff_EnregistreUnJourOff()
    {
        // Arrange
        var contexte = new Contexte();
        var headers = new[] { "Matricule", "2026-08-31" };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["2026-08-31"] = "OFF" }
        };

        // Act
        await contexte.Service.ImportExcelAsync(rows, headers);

        // Assert
        var ligne = contexte.Planning.Should().ContainSingle().Which;
        ligne.OffP.Should().BeTrue();
        ligne.Ha.Should().BeNull();
        ligne.Hd.Should().BeNull();
        ligne.NoShift.Should().BeNull();
        ligne.Pause.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task ImportExcel_CelluleVide_Ignore()
    {
        // Arrange
        var contexte = new Contexte();
        var headers = new[] { "Matricule", "2026-08-31" };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["2026-08-31"] = "" }
        };

        // Act
        var resultat = await contexte.Service.ImportExcelAsync(rows, headers);

        // Assert
        resultat.Imported.Should().Be(0);
        contexte.Planning.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateFromMatriculeCsv_Nuit_EcritHDLeLendemain()
    {
        // Arrange
        var contexte = new Contexte();
        var lignes = new[]
        {
            new[] { "Matricule", "2026-08-31", "22:00", "00:00", "06:00" },
            new[] { "001", "2026-08-31", "22:00", "00:00", "06:00" }
        };

        // Act
        var resultat = await contexte.Service.GenerateFromMatriculeCsvAsync(lignes);

        // Assert
        resultat.Imported.Should().Be(1);
        var ligne = contexte.Planning.Should().ContainSingle().Which;
        ligne.Ha.Should().Be(Lundi.AddHours(22));
        ligne.Hd.Should().Be(Lundi.AddDays(1).AddHours(6));
        ligne.Pause.Should().Be(TimeSpan.Zero);
        ligne.NoShift.Should().BeNull();
    }
}
