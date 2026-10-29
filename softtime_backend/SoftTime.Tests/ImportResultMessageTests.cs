using AutoMapper;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

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
                PAUSE = new TimeSpan(0, 30, 0),
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTEUSE = "PTE_TEST"
            });
            Uow.Get<T_CAT_SAL>().Items.Add(new T_CAT_SAL
            {
                ID = 1, MATRICULE_SAGE = 1, ID_CATEG = 10,
                BDD_SAGE = "SAGE_TEST", BDD_POINTEUSE = "PTE_TEST"
            });
            Uow.Get<T_PLANNING>().Items.Add(new T_PLANNING
            {
                NoSHIFT = 1, HA = new TimeSpan(8, 0, 0), PAUSE = new TimeSpan(0, 30, 0),
                HD = new TimeSpan(17, 0, 0), Intitule = "Journée"
            });

            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser());
            var external = new FakeExternalSourceService();
            Timekeeping = new TimekeepingService(Uow, tenant, external, new HistoryArchiveService(Uow, tenant));
            Planning = new PlanningService(Uow, tenant, new FakePlanningStatisticsReader());
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
            Catalog = new CatalogService(Uow, mapper, tenant, external, null!);
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

        var resultat = await contexte.Catalog.ImportAffectationsExcelAsync(rows);

        Assert.Equal(1, resultat.Imported);
        Assert.Equal(2, resultat.Skipped);
        Assert.Contains("1 affectation(s) importée(s), 2 ligne(s) ignorée(s)", resultat.Message);
        Assert.Contains("1 catégorie(s) introuvable(s) : Inconnue", resultat.Message);
        Assert.Contains("1 matricule(s) introuvable(s) : 9999", resultat.Message);
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

        var resultat = await contexte.Catalog.ImportCorrespondencesCsvAsync(lignes);

        Assert.Equal(1, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Equal("1 correspondance(s) importée(s), 0 ligne(s) ignorée(s).", resultat.Message);
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

        Assert.Equal(1, resultat.Imported);
        Assert.Equal(1, resultat.Skipped);
        Assert.Equal("1 shift(s) importé(s), 1 ligne(s) ignorée(s).", resultat.Message);
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

        Assert.Equal(1, resultat.Imported);
        Assert.Equal(1, resultat.Skipped);
        Assert.Equal("1 pointage(s) importé(s), 1 ligne(s) ignorée(s).", resultat.Message);
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

        Assert.Equal(0, resultat.Imported);
        Assert.Equal(2, resultat.Skipped);
        Assert.Contains("Aucun pointage valide : 2 ligne(s) ignorée(s)", resultat.Message);
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

        Assert.Equal(0, resultat.Imported);
        Assert.Equal(1, resultat.Skipped);
        Assert.Contains("0 correction(s) importée(s), 1 ligne(s) ignorée(s) (matricule manquant).", resultat.Message);
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

        Assert.Equal(0, resultat.Imported);
        Assert.Equal(1, resultat.Skipped);
        Assert.Equal("0 ligne(s) de planning importée(s), 1 élément(s) ignoré(s).", resultat.Message);
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

        Assert.Equal(1, resultat.Imported);
        Assert.Equal(0, resultat.Skipped);
        Assert.Equal("1 ligne(s) de planning importée(s), 0 élément(s) ignoré(s).", resultat.Message);
    }
}
