using System.Text;
using AutoMapper;
using SoftTime.Application.ImportTemplates;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Excel;

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
        public TimekeepingService Timekeeping { get; }
        public PlanningService Planning { get; }
        public CatalogService Catalog { get; }

        public Contexte()
        {
            Uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Get<T_BDD_POINTEUSE>().Items.Add(new T_BDD_POINTEUSE { ID = 1, NOM_BD = "PTE_TEST", ACTIVE = true });
            Uow.Get<T_CARDPAIE>().Items.AddRange(new[]
            {
                new T_CARDPAIE
                {
                    ID = 1, SAGE_MATRICULE = "04285", SAGE_BDD = "SAGE_TEST",
                    POINTEUSE_BDD = "PTE_TEST", POINTEUSE_NUMERO = "00123"
                },
                new T_CARDPAIE
                {
                    ID = 2, SAGE_MATRICULE = "04301", SAGE_BDD = "SAGE_TEST",
                    POINTEUSE_BDD = "PTE_TEST", POINTEUSE_NUMERO = "00124"
                }
            });
            Uow.Get<T_CATEGORIE>().Items.Add(new T_CATEGORIE
            {
                IDCATEGORIE = 10,
                INTITULE = "Sédentaire",
                HEURESEMAINE = 40m,
                PAUSE = Pause,
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTEUSE = "PTE_TEST"
            });
            Uow.Get<T_CAT_SAL>().Items.Add(new T_CAT_SAL
            {
                ID = 1, MATRICULE_SAGE = 1, ID_CATEG = 10,
                BDD_SAGE = "SAGE_TEST", BDD_POINTEUSE = "PTE_TEST"
            });
            Uow.Get<T_PLANNING>().Items.AddRange(new[]
            {
                new T_PLANNING { NoSHIFT = 1, HA = Huit, PAUSE = Pause, HD = DixSept, Intitule = "Journée" },
                new T_PLANNING { NoSHIFT = 2, HA = VingtDeux, PAUSE = Pause, HD = Six, Intitule = "Nuit" }
            });

            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser());
            var external = new FakeExternalSourceService();
            Timekeeping = new TimekeepingService(Uow, tenant, external, new HistoryArchiveService(Uow, tenant));
            Planning = new PlanningService(Uow, tenant, new FakePlanningStatisticsReader());
            // La découverte de bases n'est pas utilisée par les imports testés ici.
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
            Catalog = new CatalogService(Uow, mapper, tenant, external, null!);
        }

        public List<T_POINTAGE> Pointages => Uow.Get<T_POINTAGE>().Items;
        public List<T_HEURECORRIGER> Corrections => Uow.Get<T_HEURECORRIGER>().Items;
        public List<T_PLANNING_SAL> PlanningSalaries => Uow.Get<T_PLANNING_SAL>().Items;
        public List<T_CARDPAIE> Cards => Uow.Get<T_CARDPAIE>().Items;
    }

    private static ImportTemplateDefinition Modele(string key)
        => ImportTemplateCatalog.Find(key, LundiReference)
           ?? throw new InvalidOperationException($"Modèle introuvable : {key}.");

    private static List<Dictionary<string, string>> LireXlsx(string key, out List<string> headers)
    {
        using var flux = new MemoryStream(ImportTemplateFile.Build(Modele(key)));
        var rows = ExcelHelper.ReadFirstSheet(flux);
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
            Assert.True(c.VALIDER_CORRECTION);
            Assert.Equal(PunchProcessingConfiguration.OrigineManuel, c.ORIGINE);
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
        Assert.All(contexte.PlanningSalaries, p => Assert.Equal(1, p.IDSAL));
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

        var resultat = await contexte.Catalog.ImportAffectationsExcelAsync(rows);

        Assert.Equal(1, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Equal("1 affectation(s) importée(s), 0 ligne(s) ignorée(s).", resultat.Message);
    }

    [Fact]
    public async Task ModeleCorrespondances_EstImportable()
    {
        var contexte = new Contexte();
        var lignes = LireCsv("correspondences");

        var resultat = await contexte.Catalog.ImportCorrespondencesCsvAsync(lignes);

        // La ligne d'en-tête est reconnue et ignorée : elle n'est pas comptée comme ligne ignorée.
        Assert.Equal(2, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Contains("2 correspondance(s) importée(s), 0 ligne(s) ignorée(s).", resultat.Message);
        Assert.Equal("00123", contexte.Cards.Single(c => c.SAGE_MATRICULE == "04285").POINTEUSE_NUMERO);
        Assert.Equal("00124", contexte.Cards.Single(c => c.SAGE_MATRICULE == "04301").POINTEUSE_NUMERO);
    }
}
