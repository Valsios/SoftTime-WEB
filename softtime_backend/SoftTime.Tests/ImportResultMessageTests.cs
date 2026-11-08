using AutoMapper;

using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>
/// Le message renvoyé par chaque import doit contenir le bilan chiffré (importés / ignorés),
/// et pour les affectations la raison de l'ignoré (catégorie ou matricule introuvable).
/// </summary>
public class ImportResultMessageTests
{
    private static readonly DateTime LundiReference = new(2026, 11, 2);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public TimekeepingServices Timekeeping { get; }
        public PlanningServices Planning { get; }
        public CatalogServices Catalog { get; }

        public Contexte()
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 1, NomBd = "PTE_TEST", Active = true });
            Uow.Get<CardPaie>().Items.AddRange(new[]
            {
                new CardPaie
                {
                    Id = 1, SageMatricule = "04285", SageBdd = "SAGE_TEST",
                    PointeuseBdd = "PTE_TEST", PointeuseNumero = "00123"
                },
                new CardPaie
                {
                    Id = 2, SageMatricule = "04301", SageBdd = "SAGE_TEST",
                    PointeuseBdd = "PTE_TEST", PointeuseNumero = "00124"
                }
            });
            Uow.Get<Categorie>().Items.Add(new Categorie
            {
                IdCategorie = 10,
                Intitule = "Sédentaire",
                HeureSemaine = 40m,
                Pause = new TimeSpan(0, 30, 0),
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST"
            });
            Uow.Get<CategorieSalarie>().Items.Add(new CategorieSalarie
            {
                Id = 1, MatriculeSage = 1, IdCateg = 10,
                BddSage = "SAGE_TEST", BddPointeuse = "PTE_TEST"
            });
            Uow.Get<Planning>().Items.Add(new Planning
            {
                NoShift = 1, Ha = new TimeSpan(8, 0, 0), Pause = new TimeSpan(0, 30, 0),
                Hd = new TimeSpan(17, 0, 0), Intitule = "Journée"
            });

            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
            var external = new FakeExternalSourceService();
            Timekeeping = new TimekeepingServices(Uow, tenant, external, new HistoryArchiveService(Uow));
            Planning = new PlanningServices(Uow, tenant, new FakePlanningStatisticsReader());
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
            Catalog = new CatalogServices(Uow, mapper, tenant, external, null!, null!, SoftTime.Tests.Http.TestRights.Service(Uow));
        }
    }

    [Fact]
    public async Task Affectations_MessageDetailleImportesIgnoresEtRaisons()
    {
        var contexte = new Contexte();
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Categorie"] = "Sédentaire", ["Matricule"] = "04285" },
            new() { ["Categorie"] = "Inconnue", ["Matricule"] = "04285" },
            new() { ["Categorie"] = "Sédentaire", ["Matricule"] = "9999" }
        };

        var resultat = await contexte.Catalog.AffectationImports.ImportAffectationsExcelAsync(rows);

        resultat.Imported.Should().Be(1);
        resultat.Skipped.Should().Be(2);
        resultat.Message.Should().Contain("1 affectation(s) importée(s), 2 ligne(s) ignorée(s)");
        resultat.Message.Should().Contain("1 catégorie(s) introuvable(s) : Inconnue");
        resultat.Message.Should().Contain("1 matricule(s) introuvable(s) : 9999");
    }

    [Fact]
    public async Task Correspondances_EnteteNonCompteEtMessageChiffre()
    {
        var contexte = new Contexte();
        var lignes = new List<string[]>
        {
            new[] { "badge", "matricule" },
            new[] { "00123", "04285" }
        };

        var resultat = await contexte.Catalog.Cards.ImportCorrespondencesCsvAsync(lignes);

        resultat.Imported.Should().Be(1);
        resultat.Skipped.Should().Be(0);
        resultat.Message.Should().Be("1 correspondance(s) importée(s), 0 ligne(s) ignorée(s).");
    }

    [Fact]
    public async Task Shifts_MessageIndiqueImportesEtIgnores()
    {
        var contexte = new Contexte();
        var rows = new List<Dictionary<string, string>>
        {
            new()
            {
                ["NoSHIFT"] = "1", ["INTITULE"] = "Journée",
                ["ENTREE"] = "08:00", ["PAUSE"] = "00:30", ["SORTIE"] = "17:00"
            },
            new() { ["NoSHIFT"] = "abc", ["INTITULE"] = "Invalide" }
        };

        var resultat = await contexte.Planning.ImportShiftsExcelAsync(rows);

        resultat.Imported.Should().Be(1);
        resultat.Skipped.Should().Be(1);
        resultat.Message.Should().Be("1 shift(s) importé(s), 1 ligne(s) ignorée(s).");
    }

    [Fact]
    public async Task PointagesExcel_MessageIndiqueImportesEtIgnores()
    {
        var contexte = new Contexte();
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "04285", ["Date"] = "2026-11-02", ["Heure"] = "08:00" },
            new() { ["Matricule"] = "04285", ["Date"] = "2026-11-02", ["Heure"] = "08:00" }
        };

        var resultat = await contexte.Timekeeping.ImportPunchesExcelAsync(rows);

        resultat.Imported.Should().Be(1);
        resultat.Skipped.Should().Be(1);
        resultat.Message.Should().Be("1 pointage(s) importé(s), 1 ligne(s) ignorée(s).");
    }

    [Fact]
    public async Task PointagesExcel_SansLigneValide_MessageExplicite()
    {
        var contexte = new Contexte();
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["Matricule"] = "", ["Date"] = "2026-11-02", ["Heure"] = "08:00" },
            new() { ["Matricule"] = "04285", ["Date"] = "pas une date", ["Heure"] = "08:00" }
        };

        var resultat = await contexte.Timekeeping.ImportPunchesExcelAsync(rows);

        resultat.Imported.Should().Be(0);
        resultat.Skipped.Should().Be(2);
        resultat.Message.Should().Contain("Aucun pointage valide : 2 ligne(s) ignorée(s)");
    }

    [Fact]
    public async Task Corrections_CompteLesLignesSansMatricule()
    {
        var contexte = new Contexte();
        var rows = new List<Dictionary<string, string>>
        {
            new()
            {
                ["Matricule"] = "", ["DateEntree"] = "2026-11-02",
                ["HeureEntree"] = "08:00", ["HeureSortie"] = "17:00"
            }
        };

        var resultat = await contexte.Timekeeping.ImportCorrectionsExcelAsync(rows);

        resultat.Imported.Should().Be(0);
        resultat.Skipped.Should().Be(1);
        resultat.Message.Should().Contain("0 correction(s) importée(s), 1 ligne(s) ignorée(s) (matricule manquant).");
    }

    [Fact]
    public async Task Planning_MessageIndiqueImportesEtIgnores()
    {
        var contexte = new Contexte();
        var headers = new List<string> { "CATEGORIE", "2026-11-02" };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["CATEGORIE"] = "Inconnue", ["2026-11-02"] = "1" }
        };

        var resultat = await contexte.Planning.ImportExcelAsync(rows, headers);

        resultat.Imported.Should().Be(0);
        resultat.Skipped.Should().Be(1);
        resultat.Message.Should().Be("0 ligne(s) de planning importée(s), 1 élément(s) ignoré(s).");
    }

    [Fact]
    public async Task Planning_LigneValide_MessageChiffre()
    {
        var contexte = new Contexte();
        var headers = new List<string> { "CATEGORIE", LundiReference.ToString("yyyy-MM-dd") };
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["CATEGORIE"] = "Sédentaire", [LundiReference.ToString("yyyy-MM-dd")] = "1" }
        };

        var resultat = await contexte.Planning.ImportExcelAsync(rows, headers);

        resultat.Imported.Should().Be(1);
        resultat.Skipped.Should().Be(0);
        resultat.Message.Should().Be("1 ligne(s) de planning importée(s), 0 élément(s) ignoré(s).");
    }
}
