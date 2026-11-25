using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.Reports;
using SoftTime.Infrastructure.Services.RequestContext;

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

    /// <summary>Calendrier de congés SAGE indisponible (base SAGE injoignable).</summary>
    private sealed class CongesIndisponibles : ISageLeaveCalendar
    {
        public Task<Dictionary<string, List<LeaveDay>>> LoadAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("SAGE injoignable");
    }

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public FakeExternalSourceService Source { get; } = new();
        public ReportServices Service { get; }

        public List<PlanningSalarie> Planning => Uow.Get<PlanningSalarie>().Items;
        public List<HeureCorrigee> Corrections => Uow.Get<HeureCorrigee>().Items;
        public List<JourFerie> Feries => Uow.Get<JourFerie>().Items;

        public Contexte(ISageLeaveCalendar? leaves = null)
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
            Service = new ReportServices(
                Uow,
                tenant,
                Source,
                leaves ?? new SageLeaveCalendar(tenant, Source),
                NullLogger<ReportContextLoader>.Instance);
        }

        /// <summary>Arbitrage d'absence sans heure (journée complète ou demi-journée) avec son code.</summary>
        public void Arbitrage(DateTime jour, string? intitule, bool am = true, bool pm = true)
            => Corrections.Add(new HeureCorrigee
            {
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST",
                MatriculeSage = "001",
                DatePointageIn = jour.Date,
                DatePointageOut = jour.Date,
                AbsenceAm = am,
                AbsencePm = pm,
                IntituleAbsence = intitule,
                ValiderCorrection = true
            });

        /// <summary>Événement SAGE d'une journée avec son libellé de catalogue (<c>T_GHR</c>).</summary>
        public void Evenement(DateTime jour, string code, string libelle)
        {
            Source.Events.Add(new ExternalEvent(code, libelle));
            Source.EmployeeEvents.Add(new ExternalEmployeeEvent(1, code, jour.Date, jour.Date, null, null));
        }

        /// <summary>Réglage de la paie de la base (<c>T_PAIE_OPTION</c>).</summary>
        public void Option(string cle, string valeur)
            => Uow.Get<PaieOption>().Items.Add(new PaieOption { BddSage = "SAGE_TEST", Cle = cle, Valeur = valeur });

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
    public async Task AbsencesAsync_AbsencesMixtes_NAfficheQueLesNonJustifiees()
    {
        // Arrange
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

        // Act
        var rows = await ctx.Service.AbsencesAsync(Contexte.Filtre(nonPointe, declaree));

        // Assert
        var row = rows.Should().ContainSingle().Which;
        row.Matricule.Should().Be("001");
        row.Nom.Should().Be("Dupont");
        row.Prenom.Should().Be("Jean");
        row.Departement.Should().Be("INFO");
        row.Date.Should().Be(nonPointe);
        row.Type.Should().Be("Non justifiée");
        row.DureeHeures.Should().Be(8m);
    }

    [Fact]
    public async Task AbsencesAsync_JourOffEtFerie_NeProduisentAucuneLigne()
    {
        // Arrange
        var ctx = new Contexte();
        var jours = JoursOuvresPasses(2);
        var jourOff = jours[0];
        var ferie = jours[1];
        ctx.Plan(jourOff, off: true);
        ctx.Plan(ferie);
        ctx.Feries.Add(new JourFerie { Date = ferie.Date, Intitule = "Férié", BddSage = "SAGE_TEST" });

        // Act
        var rows = await ctx.Service.AbsencesAsync(Contexte.Filtre(jourOff, ferie));

        // Assert
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task AbsencesAsync_FiltreDepartement_NeGardeQueCeDepartement()
    {
        // Arrange
        var ctx = new Contexte();
        var jour = JoursOuvresPasses(1)[0];
        ctx.Plan(jour);

        // Act
        // La comparaison ignore la casse et les espaces de saisie.
        var correspondant = await ctx.Service.AbsencesAsync(Contexte.Filtre(jour, jour, " info "));

        // Assert
        correspondant.Should().ContainSingle();
        var autre = await ctx.Service.AbsencesAsync(Contexte.Filtre(jour, jour, "RH"));
        autre.Should().BeEmpty();
    }

    [Fact]
    public async Task AbsencesPagedAsync_FiltreEtPagination_FiltreAvantDePaginer()
    {
        // Arrange
        var ctx = new Contexte();
        var jours = JoursOuvresPasses(2);
        var nonPointe = jours[0];
        var conge = jours[1];
        ctx.Plan(nonPointe);
        ctx.Plan(conge);
        ctx.Conge(conge);

        // Act
        var page = await ctx.Service.AbsencesPagedAsync(Contexte.Filtre(nonPointe, conge), page: 1, pageSize: 50);

        // Assert
        page.Total.Should().Be(1);
        var row = page.Items.Should().ContainSingle().Which;
        ((SoftTime.Domain.Services.AbsenceReportRow)row).Type.Should().Be("Non justifiée");
    }

    // ------------------------------------------------------------------------------------------------
    // État « Absences justifiées » (informatif)
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AbsencesJustifieesAsync_EvenementEtArbitrage_AfficheLeMotifEtLEtatAbsencesResteIdentique()
    {
        // Arrange
        var ctx = new Contexte();
        var jours = JoursOuvresPasses(3);
        var nonPointe = jours[0];
        var conge = jours[1];
        var arbitre = jours[2];
        ctx.Plan(nonPointe);
        ctx.Plan(conge);
        ctx.Plan(arbitre);
        ctx.Evenement(conge, "0410", "CONGE");
        ctx.Arbitrage(arbitre, "PERMISSION");
        var filtre = Contexte.Filtre(nonPointe, arbitre);

        // Act
        var justifiees = await ctx.Service.Absences.AbsencesJustifieesAsync(filtre);
        var nonJustifiees = await ctx.Service.AbsencesAsync(filtre);

        // Assert
        justifiees.Select(r => (r.Date, r.Type, r.Periode, r.DureeHeures, r.Motif)).Should().Equal(
            (conge, "Événement SAGE", "Journée", (decimal?)8m, (string?)"CONGE"),
            (arbitre, "Arbitrage", "Journée", (decimal?)8m, (string?)"PERMISSION"));
        // Non-régression : l'état « Absences » ne garde que le jour non pointé, comme avant.
        var ligne = nonJustifiees.Should().ContainSingle().Which;
        ligne.Date.Should().Be(nonPointe);
        ligne.Type.Should().Be("Non justifiée");
        ligne.Motif.Should().BeNull();
    }

    [Fact]
    public async Task AbsencesJustifieesAsync_Evenement0460EtArbitrageSansCode_RestentHorsDeLEtat()
    {
        // Arrange
        var ctx = new Contexte();
        var jours = JoursOuvresPasses(2);
        ctx.Plan(jours[0]);
        ctx.Plan(jours[1]);
        ctx.Evenement(jours[0], "0460", "ABSENCE NON JUSTIFIEE");
        ctx.Arbitrage(jours[1], null);

        // Act
        var rows = await ctx.Service.Absences.AbsencesJustifieesAsync(Contexte.Filtre(jours[0], jours[1]));

        // Assert
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task AbsencesJustifieesAsync_ReglageSansCodeNonCompte_ListeLArbitrageSansCode()
    {
        // Arrange
        var ctx = new Contexte();
        var jour = JoursOuvresPasses(1)[0];
        ctx.Plan(jour);
        ctx.Arbitrage(jour, null);
        // Réglage de la base : seul « ABSENCE NON JUSTIFIEE » est compté, l'arbitrage sans code ne l'est plus.
        ctx.Option(PayrollOptions.AbsenceSansCodeCompte, "0");
        ctx.Option(PayrollOptions.CleAbsenceCode(1), "ABSENCE NON JUSTIFIEE");

        // Act
        var rows = await ctx.Service.Absences.AbsencesJustifieesAsync(Contexte.Filtre(jour, jour));

        // Assert
        var row = rows.Should().ContainSingle().Which;
        row.Type.Should().Be("Arbitrage");
        row.Motif.Should().Be("Motif non renseigné");
    }

    [Fact]
    public async Task AbsencesJustifieesAsync_FiltreDepartement_NeGardeQueCeDepartement()
    {
        // Arrange
        var ctx = new Contexte();
        var jour = JoursOuvresPasses(1)[0];
        ctx.Plan(jour);
        ctx.Evenement(jour, "0410", "CONGE");

        // Act
        var correspondant = await ctx.Service.Absences.AbsencesJustifieesAsync(Contexte.Filtre(jour, jour, " info "));
        var autre = await ctx.Service.Absences.AbsencesJustifieesAsync(Contexte.Filtre(jour, jour, "RH"));

        // Assert
        correspondant.Should().ContainSingle();
        autre.Should().BeEmpty();
    }

    [Fact]
    public async Task AbsencesJustifieesAsync_FiltreMatricules_NeGardeQueLaPlage()
    {
        // Arrange
        var ctx = new Contexte();
        var jour = JoursOuvresPasses(1)[0];
        ctx.Plan(jour);
        ctx.Evenement(jour, "0410", "CONGE");

        // Act
        var dansLaPlage = await ctx.Service.Absences.AbsencesJustifieesAsync(new ReportFilter("001", "001", jour, jour, null));
        var horsPlage = await ctx.Service.Absences.AbsencesJustifieesAsync(new ReportFilter("002", "009", jour, jour, null));

        // Assert
        dansLaPlage.Should().ContainSingle();
        horsPlage.Should().BeEmpty();
    }

    [Fact]
    public async Task AbsencesJustifieesAsync_PeriodeFuture_RetourneVide()
    {
        // Arrange
        var ctx = new Contexte();
        var demain = DateTime.Today.AddDays(1);

        // Act
        var rows = await ctx.Service.Absences.AbsencesJustifieesAsync(Contexte.Filtre(demain, demain));

        // Assert
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task AbsencesJustifieesAsync_CongesSageIndisponibles_LeveUneErreurExplicite()
    {
        // Arrange
        var ctx = new Contexte(new CongesIndisponibles());
        var jour = JoursOuvresPasses(1)[0];
        ctx.Plan(jour);

        // Act
        var justifiees = () => ctx.Service.Absences.AbsencesJustifieesAsync(Contexte.Filtre(jour, jour));
        var paged = () => ctx.Service.PagedAsync("absences-justifiees", Contexte.Filtre(jour, jour), 1, 50);

        // Assert
        (await justifiees.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("congés SAGE sont indisponibles");
        await paged.Should().ThrowAsync<DomainException>();
        // L'état « Absences » actuel garde son comportement : il ne lève pas d'erreur.
        (await ctx.Service.AbsencesAsync(Contexte.Filtre(jour, jour))).Should().ContainSingle();
    }

    [Fact]
    public async Task PagedAsync_AbsencesJustifiees_FiltreAvantDePaginerEtPagine()
    {
        // Arrange
        var ctx = new Contexte();
        var jours = JoursOuvresPasses(3);
        ctx.Plan(jours[0]);
        ctx.Plan(jours[1]);
        ctx.Plan(jours[2]);
        ctx.Evenement(jours[0], "0410", "CONGE");
        ctx.Arbitrage(jours[1], "PERMISSION");
        var filtre = Contexte.Filtre(jours[0], jours[2]);

        // Act
        var premierePage = await ctx.Service.PagedAsync("absences-justifiees", filtre, page: 1, pageSize: 1);
        var secondePage = await ctx.Service.PagedAsync("absences-justifiees", filtre, page: 2, pageSize: 1);
        var recherche = await ctx.Service.PagedAsync("absences-justifiees", filtre with { Search = "permission" }, page: 1, pageSize: 50);

        // Assert
        premierePage.Total.Should().Be(2);
        ((SoftTime.Domain.Services.AbsenceReportRow)premierePage.Items.Should().ContainSingle().Which).Motif.Should().Be("CONGE");
        ((SoftTime.Domain.Services.AbsenceReportRow)secondePage.Items.Should().ContainSingle().Which).Motif.Should().Be("PERMISSION");
        recherche.Total.Should().Be(1);
    }
}
