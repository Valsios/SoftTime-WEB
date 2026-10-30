using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

/// <summary>
/// Contrat de l'état des absences : seules les absences <b>non justifiées</b> sont affichées.
/// Les congés SAGE, jours fériés, week-ends, jours OFF et absences déclarées servent uniquement à
/// exclure la journée et ne doivent produire aucune ligne dans l'état (écran paginé comme export).
/// </summary>
public class ReportServiceTests
{
    /// <summary>Derniers jours ouvrés strictement avant aujourd'hui (l'état est plafonné à la veille).</summary>
    private static List<DateTime> JoursOuvresPasses(int nombre)
    {
        var jours = new List<DateTime>();
        var jour = DateTime.Today.AddDays(-1);
        while (jours.Count < nombre)
        {
            if (jour.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                jours.Add(jour.Date);
            jour = jour.AddDays(-1);
        }
        jours.Reverse();
        return jours;
    }

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public FakeExternalSourceService Source { get; } = new();
        public ReportService Service { get; }

        public List<T_PLANNING_SAL> Planning => Uow.Get<T_PLANNING_SAL>().Items;
        public List<T_HEURECORRIGER> Corrections => Uow.Get<T_HEURECORRIGER>().Items;
        public List<T_FERIE> Feries => Uow.Get<T_FERIE>().Items;

        public Contexte()
        {
            Uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Get<T_CARDPAIE>().Items.Add(new T_CARDPAIE
            {
                ID = 1,
                SAGE_MATRICULE = "001",
                SAGE_NOM = "Dupont",
                SAGE_PRENOM = "Jean",
                DEPARTEMENT = "INFO",
                SAGE_BDD = "SAGE_TEST",
                POINTEUSE_BDD = "PTE_TEST"
            });
            Uow.Get<T_CAT_SAL>().Items.Add(new T_CAT_SAL
            {
                ID = 1,
                MATRICULE_SAGE = 1,
                ID_CATEG = 10,
                BDD_SAGE = "SAGE_TEST"
            });
            Uow.Get<T_TOLERANCE>().Items.Add(new T_TOLERANCE
            {
                IDCATEGORIE = 10,
                TOLERANCE = 10,
                TYPES_TOL = false
            });

            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser());
            Source.Employees.Add(new ExternalEmployee("001", 1, "Dupont", "Jean", null, false));
            Service = new ReportService(
                Uow,
                tenant,
                Source,
                new SageLeaveCalendar(tenant, Source),
                NullLogger<ReportService>.Instance);
        }

        /// <summary>Journée planifiée 08:00–17:00 avec 1 h de pause (8 h attendues), ou jour OFF.</summary>
        public void Plan(DateTime jour, bool off = false)
            => Planning.Add(new T_PLANNING_SAL
            {
                IDSAL = 1,
                DateP = jour.Date,
                HA = off ? null : jour.Date.AddHours(8),
                HD = off ? null : jour.Date.AddHours(17),
                Pause = off ? TimeSpan.Zero : TimeSpan.FromHours(1),
                OffP = off,
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTEUSE = "PTE_TEST"
            });

        /// <summary>Heure corrigée déclarant l'absence (type « Arbitrée GTA »).</summary>
        public void AbsenceDeclaree(DateTime jour)
            => Corrections.Add(new T_HEURECORRIGER
            {
                BDD_SAGE = "SAGE_TEST",
                BDD_POINTEUSE = "PTE_TEST",
                MATRICULE_SAGE = "001",
                DATE_POINTAGE_IN = jour.Date,
                DATE_POINTAGE_OUT = jour.Date,
                HENTREE = TimeSpan.FromHours(8),
                HSORTIE = TimeSpan.FromHours(17),
                ABSENCE_AM = true,
                VALIDER_CORRECTION = true
            });

        /// <summary>Congé SAGE d'une journée (type « Congé SAGE »).</summary>
        public void Conge(DateTime jour)
        {
            Source.Events.Add(new ExternalEvent("CP", "Congé payé"));
            Source.EmployeeEvents.Add(new ExternalEmployeeEvent(1, "CP", jour.Date, jour.Date, null, null));
        }

        public static ReportFilter Filtre(DateTime from, DateTime to, string? departement = null)
            => new(null, null, from, to, departement);
    }

    [Fact]
    public async Task AbsencesAsync_NAfficheQueLesNonJustifiees()
    {
        var ctx = new Contexte();
        var jours = JoursOuvresPasses(3);
        var nonPointe = jours[0];
        var conge = jours[1];
        var declaree = jours[2];

        ctx.Plan(nonPointe);
        ctx.Plan(conge);
        ctx.Plan(declaree);
        ctx.Conge(conge);
        ctx.AbsenceDeclaree(declaree);

        var rows = await ctx.Service.AbsencesAsync(Contexte.Filtre(nonPointe, declaree));

        var row = Assert.Single(rows);
        Assert.Equal("001", row.Matricule);
        Assert.Equal("Dupont", row.Nom);
        Assert.Equal("Jean", row.Prenom);
        Assert.Equal("INFO", row.Departement);
        Assert.Equal(nonPointe, row.Date);
        Assert.Equal("Non justifiée", row.Type);
        Assert.Equal(8m, row.DureeHeures);
    }

    [Fact]
    public async Task AbsencesAsync_JourOffEtFerie_NeProduisentAucuneLigne()
    {
        var ctx = new Contexte();
        var jours = JoursOuvresPasses(2);
        var jourOff = jours[0];
        var ferie = jours[1];

        ctx.Plan(jourOff, off: true);
        ctx.Plan(ferie);
        ctx.Feries.Add(new T_FERIE { DATE = ferie.Date, INTITULE = "Férié", BDD_SAGE = "SAGE_TEST" });

        var rows = await ctx.Service.AbsencesAsync(Contexte.Filtre(jourOff, ferie));

        Assert.Empty(rows);
    }

    [Fact]
    public async Task AbsencesAsync_FiltreParDepartement()
    {
        var ctx = new Contexte();
        var jour = JoursOuvresPasses(1)[0];
        ctx.Plan(jour);

        // La comparaison ignore la casse et les espaces de saisie.
        var correspondant = await ctx.Service.AbsencesAsync(Contexte.Filtre(jour, jour, " info "));
        Assert.Single(correspondant);

        var autre = await ctx.Service.AbsencesAsync(Contexte.Filtre(jour, jour, "RH"));
        Assert.Empty(autre);
    }

    [Fact]
    public async Task AbsencesPagedAsync_FiltreAvantPagination()
    {
        var ctx = new Contexte();
        var jours = JoursOuvresPasses(2);
        var nonPointe = jours[0];
        var conge = jours[1];

        ctx.Plan(nonPointe);
        ctx.Plan(conge);
        ctx.Conge(conge);

        var page = await ctx.Service.AbsencesPagedAsync(Contexte.Filtre(nonPointe, conge), page: 1, pageSize: 50);

        Assert.Equal(1, page.Total);
        var row = Assert.Single(page.Items);
        Assert.Equal("Non justifiée", ((SoftTime.Domain.Services.AbsenceReportRow)row).Type);
    }
}
