using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services;

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

        public List<PlanningSalarie> Planning => Uow.Get<PlanningSalarie>().Items;
        public List<HeureCorrigee> Corrections => Uow.Get<HeureCorrigee>().Items;
        public List<JourFerie> Feries => Uow.Get<JourFerie>().Items;

        public Contexte()
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Get<CardPaie>().Items.Add(new CardPaie
            {
                Id = 1,
                SageMatricule = "001",
                SageNom = "Dupont",
                SagePrenom = "Jean",
                Departement = "INFO",
                SageBdd = "SAGE_TEST",
                PointeuseBdd = "PTE_TEST"
            });
            Uow.Get<CategorieSalarie>().Items.Add(new CategorieSalarie
            {
                Id = 1,
                MatriculeSage = 1,
                IdCateg = 10,
                BddSage = "SAGE_TEST"
            });
            Uow.Get<ToleranceRetard>().Items.Add(new ToleranceRetard
            {
                IdCategorie = 10,
                Tolerance = 10,
                TypesTol = false
            });

            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
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
            => Planning.Add(new PlanningSalarie
            {
                IdSal = 1,
                DateP = jour.Date,
                Ha = off ? null : jour.Date.AddHours(8),
                Hd = off ? null : jour.Date.AddHours(17),
                Pause = off ? TimeSpan.Zero : TimeSpan.FromHours(1),
                OffP = off,
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST"
            });

        /// <summary>Heure corrigée déclarant l'absence (type « Arbitrée GTA »).</summary>
        public void AbsenceDeclaree(DateTime jour)
            => Corrections.Add(new HeureCorrigee
            {
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST",
                MatriculeSage = "001",
                DatePointageIn = jour.Date,
                DatePointageOut = jour.Date,
                HEntree = TimeSpan.FromHours(8),
                HSortie = TimeSpan.FromHours(17),
                AbsenceAm = true,
                ValiderCorrection = true
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
        ctx.Feries.Add(new JourFerie { Date = ferie.Date, Intitule = "Férié", BddSage = "SAGE_TEST" });

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
