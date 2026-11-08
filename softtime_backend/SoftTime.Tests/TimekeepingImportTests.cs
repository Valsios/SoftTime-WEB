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
    public async Task ImportPunchesExcelAsync_LignesIdentiquesDuFichier_LesDedoublonne()
    {
        // Arrange
        var contexte = new Contexte();
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["Date"] = "2026-08-31", ["Heure"] = "08:00" },
            new() { ["Matricule"] = "001", ["Date"] = "2026-08-31", ["Heure"] = "08:00" }
        };

        // Act
        var resultat = await contexte.Service.ImportPunchesExcelAsync(rows);

        // Assert
        resultat.Imported.Should().Be(1);
        resultat.Skipped.Should().Be(1);
        resultat.Message.Should().Be("1 pointage(s) importé(s), 1 ligne(s) ignorée(s).");
        contexte.Pointages.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportPunchesExcelAsync_LignesDejaEnBase_LesDedoublonne()
    {
        // Arrange
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

        // Act
        var resultat = await contexte.Service.ImportPunchesExcelAsync(rows);

        // Assert
        resultat.Imported.Should().Be(0);
        resultat.Skipped.Should().Be(1);
        contexte.Pointages.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportFromClock_DeuxPasses_DedoublonneEnMultiPointage()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.External.Users.Add(new ExternalPunchUser(1, "B1", "001", "Test"));
        contexte.External.Punches.Add(new ExternalPunch(1, Lundi.AddHours(8), "IO"));
        var requete = new ImportPunchesRequest(null, null, Lundi, Lundi);
        var premier = await contexte.Service.ImportFromClockAsync(requete);

        // Act
        var second = await contexte.Service.ImportFromClockAsync(requete);

        // Assert
        premier.Imported.Should().Be(1);
        second.Imported.Should().Be(0);
        second.Skipped.Should().Be(1);
        premier.Message.Should().Be("1 pointage(s) importé(s), 0 ignoré(s).");
        second.Message.Should().Be("0 pointage(s) importé(s), 1 ignoré(s).");
        contexte.Pointages.Should().ContainSingle();
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
        // Arrange
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

        // Act
        contexte.Uow.Get<HeureCorrigee>().Items.Add(existante);

        // Assert
        await FluentActions.Awaiting(() => contexte.Service.SaveCorrectionAsync(
            new CorrectedHourDto { Matricule = "001", DateIn = Lundi.AddDays(1), HEntree = new TimeSpan(8, 0, 0), HSortie = new TimeSpan(20, 0, 0) })).Should().ThrowExactlyAsync<ConflictException>();
        await FluentActions.Awaiting(() => contexte.Service.DeleteCorrectionAsync(5)).Should().ThrowExactlyAsync<ConflictException>();
        var import = await contexte.Service.ImportCorrectionsExcelAsync(new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "001", ["DateEntree"] = "2026-09-01", ["HeureEntree"] = "08:00", ["HeureSortie"] = "20:00" }
        });
        import.Imported.Should().Be(0);
        import.Skipped.Should().Be(1);
        contexte.Uow.Get<HeureCorrigee>().Items.Should().ContainSingle().Which.Should().BeSameAs(existante);
    }

    [Fact]
    public async Task ValidationAnomalie_AbsenceJourneeComplete_SansHeures_DevientUneAbsence()
    {
        // Arrange
        var contexte = new Contexte();
        var anomalie = AnomalieAbsence(contexte, Lundi);

        // Act
        await contexte.Service.ValidateAnomaliesAsync(new[] { anomalie.Id });

        // Assert
        var correction = contexte.Uow.Get<HeureCorrigee>().Items.Should().ContainSingle().Which;
        correction.AbsenceAm.Should().BeTrue();
        correction.AbsencePm.Should().BeTrue();
        correction.HEntree.Should().BeNull();
        correction.HSortie.Should().BeNull();
        contexte.Uow.Get<HeureAnomalie>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidationAnomalie_SemaineValidee_EstRefusee()
    {
        // Arrange
        var contexte = new Contexte();
        ValiderSemaine(contexte, Lundi);
        var anomalie = AnomalieAbsence(contexte, Lundi.AddDays(2));

        // Act
        Func<Task> act = () => contexte.Service.ValidateAnomaliesAsync(new[] { anomalie.Id });

        // Assert
        await act.Should().ThrowExactlyAsync<ConflictException>();
        contexte.Uow.Get<HeureCorrigee>().Items.Should().BeEmpty();
    }
}
