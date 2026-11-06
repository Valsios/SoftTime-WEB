using System.Text;

using AutoMapper;

using SoftTime.Application.ImportTemplates;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Excel;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>
/// Vérifie que chaque modèle téléchargeable est réellement importable par l'endpoint correspondant :
/// mêmes en-têtes que les parseurs, aucune ligne ignorée. C'est le garde-fou qui empêche les
/// modèles de dériver du code d'import.
/// </summary>
public class ImportTemplateImportTests
{
    private static readonly DateTime LundiReference = new(2026, 11, 2); // lundi
    private static readonly TimeSpan Huit = new(8, 0, 0);
    private static readonly TimeSpan DixSept = new(17, 0, 0);
    private static readonly TimeSpan VingtDeux = new(22, 0, 0);
    private static readonly TimeSpan Six = new(6, 0, 0);
    private static readonly TimeSpan Pause = new(0, 30, 0);

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
                Pause = Pause,
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST"
            });
            Uow.Get<CategorieSalarie>().Items.Add(new CategorieSalarie
            {
                Id = 1, MatriculeSage = 1, IdCateg = 10,
                BddSage = "SAGE_TEST", BddPointeuse = "PTE_TEST"
            });
            Uow.Get<Planning>().Items.AddRange(new[]
            {
                new Planning { NoShift = 1, Ha = Huit, Pause = Pause, Hd = DixSept, Intitule = "Journée" },
                new Planning { NoShift = 2, Ha = VingtDeux, Pause = Pause, Hd = Six, Intitule = "Nuit" }
            });

            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
            var external = new FakeExternalSourceService();
            Timekeeping = new TimekeepingServices(Uow, tenant, external, new HistoryArchiveService(Uow, tenant));
            Planning = new PlanningServices(Uow, tenant, new FakePlanningStatisticsReader());
            // La découverte de bases n'est pas utilisée par les imports testés ici.
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
            Catalog = new CatalogServices(Uow, mapper, tenant, external, null!, null!, SoftTime.Tests.Http.TestRights.Service(Uow));
        }

        public List<Pointage> Pointages => Uow.Get<Pointage>().Items;
        public List<HeureCorrigee> Corrections => Uow.Get<HeureCorrigee>().Items;
        public List<PlanningSalarie> PlanningSalaries => Uow.Get<PlanningSalarie>().Items;
        public List<CardPaie> Cards => Uow.Get<CardPaie>().Items;
    }

    private static ImportTemplateDefinition Modele(string key)
        => ImportTemplateCatalog.Find(key, LundiReference)
           ?? throw new InvalidOperationException($"Modèle introuvable : {key}.");

    private static List<Dictionary<string, string>> LireXlsx(string key, out List<string> headers)
    {
        using var flux = new MemoryStream(ImportTemplateFile.Build(Modele(key)));
        var rows = ExcelWorkbook.ReadFirstSheet(flux);
        headers = rows.Count > 0 ? rows[0].Keys.ToList() : new List<string>();
        return rows;
    }

    private static IReadOnlyList<string[]> LireCsv(string key)
        => new UTF8Encoding(false)
            .GetString(ImportTemplateFile.Build(Modele(key)))
            .TrimStart('\uFEFF')
            .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ligne => ligne.Split(';'))
            .ToList();

    [Fact]
    public async Task ModelePointages_EstImportable()
    {
        var contexte = new Contexte();
        var rows = LireXlsx("punches", out _);

        var resultat = await contexte.Timekeeping.ImportPunchesExcelAsync(rows);

        Assert.Equal(rows.Count, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Contains($"{rows.Count} pointage(s) importé(s), 0 ligne(s) ignorée(s).", resultat.Message);
        Assert.Equal(rows.Count, contexte.Pointages.Count);
    }

    [Fact]
    public async Task ModeleCorrections_EstImportable()
    {
        var contexte = new Contexte();
        var rows = LireXlsx("corrections", out _);

        var resultat = await contexte.Timekeeping.ImportCorrectionsExcelAsync(rows);

        Assert.Equal(rows.Count, resultat.Imported);
        Assert.Contains($"{rows.Count} correction(s) importée(s), 0 ligne(s) ignorée(s).", resultat.Message);
        Assert.Equal(rows.Count, contexte.Corrections.Count);
        Assert.All(contexte.Corrections, c =>
        {
            Assert.True(c.ValiderCorrection);
            Assert.Equal(PunchProcessingConfiguration.OrigineManuel, c.Origine);
        });
    }

    [Fact]
    public async Task ModeleShifts_EstImportable()
    {
        var contexte = new Contexte();
        var rows = LireXlsx("shifts", out _);

        var resultat = await contexte.Planning.ImportShiftsExcelAsync(rows);

        Assert.Equal(2, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Contains("2 shift(s) importé(s), 0 ligne(s) ignorée(s).", resultat.Message);
    }

    [Fact]
    public async Task ModelePlanningMatricule_EstImportable()
    {
        var contexte = new Contexte();
        var rows = LireXlsx("planning-matricule", out var headers);

        var resultat = await contexte.Planning.ImportExcelAsync(rows, headers);

        Assert.Equal(14, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Contains("14 ligne(s) de planning importée(s), 0 élément(s) ignoré(s).", resultat.Message);
        Assert.Equal(14, contexte.PlanningSalaries.Count);
    }

    [Fact]
    public async Task ModelePlanningCategorie_EstImportable()
    {
        var contexte = new Contexte();
        var rows = LireXlsx("planning-categorie", out var headers);

        var resultat = await contexte.Planning.ImportExcelAsync(rows, headers);

        Assert.Equal(7, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Contains("7 ligne(s) de planning importée(s), 0 élément(s) ignoré(s).", resultat.Message);
        Assert.All(contexte.PlanningSalaries, p => Assert.Equal(1, p.IdSal));
    }

    [Fact]
    public async Task ModeleGenerationCategorie_EstImportable()
    {
        var contexte = new Contexte();
        var lignes = LireCsv("planning-generate-category");

        var resultat = await contexte.Planning.GenerateFromCategoryCsvAsync(lignes);

        Assert.Equal(7, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
    }

    [Fact]
    public async Task ModeleGenerationMatricule_EstImportable()
    {
        var contexte = new Contexte();
        var lignes = LireCsv("planning-generate-matricule");

        var resultat = await contexte.Planning.GenerateFromMatriculeCsvAsync(lignes);

        Assert.Equal(7, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
    }

    [Fact]
    public async Task ModeleAffectations_EstImportable()
    {
        var contexte = new Contexte();
        var rows = LireXlsx("affectations", out _);

        var resultat = await contexte.Catalog.Affectations.ImportAffectationsExcelAsync(rows);

        Assert.Equal(1, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Equal("1 affectation(s) importée(s), 0 ligne(s) ignorée(s).", resultat.Message);
    }

    [Fact]
    public async Task ModeleCorrespondances_EstImportable()
    {
        var contexte = new Contexte();
        var lignes = LireCsv("correspondences");

        var resultat = await contexte.Catalog.Cards.ImportCorrespondencesCsvAsync(lignes);

        // La ligne d'en-tête est reconnue et ignorée : elle n'est pas comptée comme ligne ignorée.
        Assert.Equal(2, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Contains("2 correspondance(s) importée(s), 0 ligne(s) ignorée(s).", resultat.Message);
        Assert.Equal("00123", contexte.Cards.Single(c => c.SageMatricule == "04285").PointeuseNumero);
        Assert.Equal("00124", contexte.Cards.Single(c => c.SageMatricule == "04301").PointeuseNumero);
    }
}
