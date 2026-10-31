using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Tests;

/// <summary>
/// Vérifie que l'ETL reconstruit les labels avec les règles métier existantes : une absence
/// non justifiée de l'état des absences doit se retrouver à l'identique dans la base analytique.
/// </summary>
public class MlDatasetParityTests
{
    private static readonly DateTime Lundi = new(2026, 9, 7);
    private static readonly DateTime Vendredi = Lundi.AddDays(4);
    private static readonly DateTime Dimanche = Lundi.AddDays(6);
    private static readonly TimeSpan Huit = new(8, 0, 0);
    private static readonly TimeSpan DixSept = new(17, 0, 0);

    [Fact]
    public async Task Les_absences_non_justifiees_sont_identiques_a_l_etat_des_absences()
    {
        var uow = new InMemoryUnitOfWork();
        var source = new FakeExternalSourceService();
        var company = new FakeCompanyContext();
        var tenant = new TenantConnectionService(uow, company, new FakeCurrentUser());
        var store = new FakeMlStore();

        uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
        uow.Get<T_CLOCK>().Items.Add(new T_CLOCK { IDPOINT = 1, MULTIPOINT = true });

        var carte = new T_CARDPAIE
        {
            ID = 1,
            SAGE_MATRICULE = "001",
            SAGE_NOM = "Dupont",
            SAGE_PRENOM = "Jean",
            SAGE_BDD = "SAGE_TEST",
            POINTEUSE_BDD = "PTE_TEST",
            DEPARTEMENT = "Atelier",
            ACTIF = true
        };
        uow.Get<T_CARDPAIE>().Items.Add(carte);
        uow.Get<T_CATEGORIE>().Items.Add(new T_CATEGORIE
        {
            IDCATEGORIE = 10,
            INTITULE = "Sédentaire",
            HEURESEMAINE = 40m,
            PAUSE = TimeSpan.FromMinutes(30),
            BDD_SAGE = "SAGE_TEST"
        });
        uow.Get<T_CAT_SAL>().Items.Add(new T_CAT_SAL
        {
            ID = 1,
            MATRICULE_SAGE = 1,
            ID_CATEG = 10,
            BDD_SAGE = "SAGE_TEST"
        });

        // Planning : lundi → vendredi, 8 h – 17 h avec 30 min de pause.
        for (var jour = Lundi; jour <= Vendredi; jour = jour.AddDays(1))
        {
            uow.Get<T_PLANNING_SAL>().Items.Add(new T_PLANNING_SAL
            {
                IDSAL = 1,
                DateP = jour,
                HA = jour + Huit,
                HD = jour + DixSept,
                Pause = TimeSpan.FromMinutes(30),
                OffP = false,
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTEUSE = "PTE_TEST"
            });
        }

        // Lundi et mercredi pointés ; mardi et jeudi absents ; vendredi absence déclarée.
        var corrections = uow.Get<T_HEURECORRIGER>().Items;
        corrections.Add(Pointage(Lundi));
        corrections.Add(Pointage(Lundi.AddDays(2)));
        corrections.Add(new T_HEURECORRIGER
        {
            BDD_SAGE = "SAGE_TEST",
            BDD_POINTEUSE = "PTE_TEST",
            MATRICULE_SAGE = "001",
            DATE_POINTAGE_IN = Vendredi,
            ABSENCE_AM = true,
            ABSENCE_PM = true,
            ORIGINE = "MANUEL"
        });

        var service = new MlDatasetService(
            uow, tenant, new SageLeaveCalendar(tenant, source), store,
            new MlOptions { WarmupDays = 0 }, NullLogger<MlDatasetService>.Instance);

        var result = await service.BuildAsync(Lundi, Dimanche, persist: true);

        var planned = store.Daily.Where(d => d.EstJourPlanifie).ToList();
        Assert.Equal(5, planned.Count);
        Assert.Equal(2, store.Daily.Count(d => d.AbsenceNonJustifiee));
        Assert.True(store.Daily.Single(d => d.Date == Vendredi).AbsenceDeclaree);
        Assert.False(store.Daily.Single(d => d.Date == Vendredi).AbsenceNonJustifiee);

        // Parité avec l'état des absences, sur la même population et la même règle.
        var absences = AbsenceReportRules.Calculer(
            uow.Get<T_PLANNING_SAL>().Items,
            new[] { carte },
            corrections,
            new Dictionary<string, List<SoftTime.Domain.Models.LeaveDay>>(),
            new HashSet<DateTime>(),
            Lundi,
            Dimanche,
            Dimanche.AddDays(1));

        Assert.Equal(
            2,
            absences.Count(a => a.Type == AbsenceReportRules.TypeNonJustifiee));

        Assert.Equal(1, store.Weekly.Count);
        Assert.False(store.Weekly[0].HSValidee);
        Assert.Equal(1, result.WeeklyRows);
    }

    private static T_HEURECORRIGER Pointage(DateTime jour) => new()
    {
        BDD_SAGE = "SAGE_TEST",
        BDD_POINTEUSE = "PTE_TEST",
        MATRICULE_SAGE = "001",
        DATE_POINTAGE_IN = jour,
        DATE_POINTAGE_OUT = jour,
        HENTREE = Huit,
        HSORTIE = DixSept,
        ORIGINE = "AUTO"
    };
}
