using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.Reports;
using SoftTime.Infrastructure.Services.RequestContext;

namespace SoftTime.Tests;

/// <summary>
/// Lot L1 (performance des rapports) : les lectures réduites en SQL par les filtres matricules / département doivent
/// donner exactement les mêmes lignes de rapport que la lecture complète suivie du filtrage en mémoire ; les tolérances ne
/// sont lues que pour les retards ; les journaux de mesure ne contiennent aucune donnée personnelle.
/// </summary>
public class ReportContextLoaderTests
{
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
        public ITenantConnectionService Tenant { get; }
        public ReportContextLoader Loader { get; }
        public List<DateTime> Jours { get; } = JoursOuvresPasses(4);

        public Contexte(ILogger<ReportContextLoader>? logger = null)
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Tenant = new TenantConnectionService(
                Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow),
                NullLogger<TenantConnectionService>.Instance);
            Loader = new ReportContextLoader(Uow, Tenant, new SageLeaveCalendar(Tenant, Source), logger ?? NullLogger<ReportContextLoader>.Instance);

            // Cinq salariés : matricules écrits avec des zéros de tête différents dans les corrections (clé « 1 », « 2 », « 10 »…).
            Carte(1, "001", "INFO");
            Carte(2, "002", "RH");
            Carte(3, "010", "INFO");
            Carte(4, "0100", "RH");
            Carte(5, "ABC1", "INFO");
            Uow.Get<CardPaie>().Items.Add(new CardPaie { Id = 6, SageMatricule = "999", Departement = "INFO", SageBdd = "AUTRE_BASE" });

            // Planning travaillé chaque jour pour chacun ; pointage seulement le 1er jour (les autres jours sont des absences).
            foreach (var carte in Uow.Get<CardPaie>().Items.Where(c => c.SageBdd == "SAGE_TEST"))
                foreach (var jour in Jours)
                    Plan(carte.Id, jour);
            Pointage("1", Jours[0]);
            Pointage("002", Jours[0]);
            Pointage("0010", Jours[0]);   // même clé que la carte « 010 »
            Pointage("100", Jours[0]);    // même clé que la carte « 0100 »
            Pointage("abc1", Jours[0]);   // même clé (casse) que la carte « ABC1 »
            Pointage("0001", Jours[1]);   // même clé que la carte « 001 »
            Pointage("777", Jours[0]);    // salarié sans carte retenue
            Uow.Get<ToleranceRetard>().Items.Add(new ToleranceRetard { IdCategorie = 10, Tolerance = 10, TypesTol = false });
            Uow.Get<CategorieSalarie>().Items.Add(new CategorieSalarie { Id = 1, MatriculeSage = 1, IdCateg = 10, BddSage = "SAGE_TEST" });
        }

        private void Carte(int id, string matricule, string departement)
            => Uow.Get<CardPaie>().Items.Add(new CardPaie
            {
                Id = id,
                SageMatricule = matricule,
                SageNom = "Nom" + id,
                Departement = departement,
                SageBdd = "SAGE_TEST",
                PointeuseBdd = "PTE_TEST"
            });

        private void Plan(int idsal, DateTime jour)
            => Uow.Get<PlanningSalarie>().Items.Add(new PlanningSalarie
            {
                Id = idsal * 100 + jour.Day,
                IdSal = idsal,
                DateP = jour,
                Ha = jour.AddHours(8),
                Hd = jour.AddHours(17),
                Pause = TimeSpan.FromHours(1),
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST"
            });

        private void Pointage(string matricule, DateTime jour)
            => Uow.Get<HeureCorrigee>().Items.Add(new HeureCorrigee
            {
                Id = Uow.Get<HeureCorrigee>().Items.Count + 1,
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST",
                MatriculeSage = matricule,
                DatePointageIn = jour,
                DatePointageOut = jour,
                HEntree = TimeSpan.FromHours(8),
                HSortie = TimeSpan.FromHours(17),
                ValiderCorrection = true
            });

        public ReportFilter Filtre(string? de = null, string? a = null, string? departement = null)
            => new(de, a, Jours[0], Jours[^1], departement);
    }

    public static TheoryData<string?, string?, string?> Filtres() => new()
    {
        { null, null, null },        // sans filtre
        { "002", "010", null },      // plage de matricules
        { null, null, "INFO" },      // département
        { "001", "010", "INFO" },    // les deux
        { "002", "002", "RH" },      // une seule carte
        { "500", "900", null },      // aucune carte : contexte vide
    };

    [Theory]
    [MemberData(nameof(Filtres))]
    public async Task Contexte_FiltresPousses_DonnentLesMemesLignesQueLeChargementCompletFiltreEnMemoire(
        string? de, string? a, string? departement)
    {
        // Arrange
        var ctx = new Contexte();
        var filtre = ctx.Filtre(de, a, departement);

        // Act
        var charge = await ctx.Loader.ChargerContexteAsync(filtre, avecTolerances: true, CancellationToken.None);

        // Assert
        // Attendu : tout ce que contient la période pour la base, restreint en mémoire aux cartes retenues.
        var cartesAttendues = ctx.Uow.Get<CardPaie>().Items
            .Where(c => c.SageBdd == "SAGE_TEST")
            .Where(c => (de is null || string.Compare(c.SageMatricule, de, StringComparison.OrdinalIgnoreCase) >= 0)
                        && (a is null || string.Compare(c.SageMatricule, a, StringComparison.OrdinalIgnoreCase) <= 0))
            .Where(c => departement is null || c.Departement == departement)
            .ToList();
        if (cartesAttendues.Count == 0)
        {
            charge.Should().BeNull();
            return;
        }

        var cles = cartesAttendues.Select(c => SoftTime.Domain.Services.MatriculeIdentity.Cle(c.SageMatricule)).ToHashSet();
        var correctionsAttendues = ctx.Uow.Get<HeureCorrigee>().Items
            .Where(h => cles.Contains(SoftTime.Domain.Services.MatriculeIdentity.Cle(h.MatriculeSage)))
            .Select(h => h.Id)
            .Order()
            .ToList();
        var planningsAttendus = ctx.Uow.Get<PlanningSalarie>().Items
            .Where(p => cartesAttendues.Any(c => c.Id == p.IdSal))
            .Select(p => p.Id)
            .Order()
            .ToList();

        charge.Should().NotBeNull();
        charge!.Cartes.Select(c => c.Id).Should().BeEquivalentTo(cartesAttendues.Select(c => c.Id));
        charge.Corrections.Select(h => h.Id).Order().Should().Equal(correctionsAttendues);
        charge.Plannings.Select(p => p.Id).Order().Should().Equal(planningsAttendus);
        charge.Mesures!.FiltresPousses.Should().Be(de is not null || a is not null || departement is not null);
    }

    [Theory]
    [MemberData(nameof(Filtres))]
    public async Task Absences_FiltresPousses_DonnentLesMemesLignesQueLeCalculCompletFiltreEnMemoire(
        string? de, string? a, string? departement)
    {
        // Arrange
        var ctx = new Contexte();
        var service = new AbsenceReportService(ctx.Tenant, ctx.Loader);
        var sansFiltre = await service.AbsencesAsync(ctx.Filtre());

        // Act
        var filtrees = await service.AbsencesAsync(ctx.Filtre(de, a, departement));

        // Assert
        var cartes = ctx.Uow.Get<CardPaie>().Items
            .Where(c => c.SageBdd == "SAGE_TEST")
            .Where(c => (de is null || string.Compare(c.SageMatricule, de, StringComparison.OrdinalIgnoreCase) >= 0)
                        && (a is null || string.Compare(c.SageMatricule, a, StringComparison.OrdinalIgnoreCase) <= 0))
            .Where(c => departement is null || c.Departement == departement)
            .Select(c => c.SageMatricule)
            .ToHashSet();
        var attendues = sansFiltre.Where(r => cartes.Contains(r.Matricule)).ToList();
        filtrees.Should().Equal(attendues);
        if (de is null && a is null && departement is null)
            filtrees.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Contexte_SansTolerances_NeLitNiCategoriesNiTolerances()
    {
        // Arrange
        var ctx = new Contexte();

        // Act
        var sans = await ctx.Loader.ChargerContexteAsync(ctx.Filtre(), avecTolerances: false, CancellationToken.None);
        var avec = await ctx.Loader.ChargerContexteAsync(ctx.Filtre(), CancellationToken.None);

        // Assert
        sans!.Tolerances.Should().BeEmpty();
        sans.CategorieParCarte.Should().BeEmpty();
        avec!.Tolerances.Should().HaveCount(1);
        avec.CategorieParCarte.Should().NotBeEmpty();
        // Le reste du contexte est identique.
        sans.Corrections.Select(h => h.Id).Should().Equal(avec.Corrections.Select(h => h.Id));
        sans.Plannings.Select(p => p.Id).Should().Equal(avec.Plannings.Select(p => p.Id));
    }

    /// <summary>Chargeur espion : enregistre si les tolérances ont été demandées.</summary>
    private sealed class ChargeurEspion : IReportContextLoader
    {
        private readonly IReportContextLoader _reel;
        public List<bool> AvecTolerances { get; } = new();

        public ChargeurEspion(IReportContextLoader reel) => _reel = reel;

        public Task<ReportContext?> ChargerContexteAsync(ReportFilter filter, CancellationToken ct)
        {
            AvecTolerances.Add(true);
            return _reel.ChargerContexteAsync(filter, ct);
        }

        public Task<ReportContext?> ChargerContexteAsync(ReportFilter filter, bool avecTolerances, CancellationToken ct)
        {
            AvecTolerances.Add(avecTolerances);
            return _reel.ChargerContexteAsync(filter, avecTolerances, ct);
        }

        public Task<AbsenceCodesComptes> ChargerCodesAbsenceAsync(CancellationToken ct) => _reel.ChargerCodesAbsenceAsync(ct);
    }

    [Fact]
    public async Task Etats_SeuleLEtatRetardsDemandeLesTolerances()
    {
        // Arrange
        var ctx = new Contexte();
        var espion = new ChargeurEspion(ctx.Loader);
        var absences = new AbsenceReportService(ctx.Tenant, espion);
        var retards = new RetardReportService(ctx.Uow, ctx.Tenant, espion);
        var filtre = ctx.Filtre();

        // Act
        await absences.AbsencesAsync(filtre);
        await absences.AbsencesPagedAsync(filtre, 1, 50);
        await absences.AbsencesJustifieesAsync(filtre);
        var apresAbsences = espion.AvecTolerances.ToList();
        await retards.CalculerRetardsAsync(filtre, CancellationToken.None);

        // Assert
        apresAbsences.Should().Equal(false, false, false);
        espion.AvecTolerances.Should().Equal(false, false, false, true);
    }

    /// <summary>Journal qui garde les messages formatés.</summary>
    private sealed class JournalMemoire<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
                Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task Journal_UneLigneParRequete_AvecLesDureesEtSansDonneePersonnelle()
    {
        // Arrange
        var ctx = new Contexte();
        // Valeurs reconnaissables : si l'une d'elles apparaît dans le journal, il fuit.
        var carte = ctx.Uow.Get<CardPaie>().Items.First(c => c.Id == 1);
        carte.SageMatricule = "MZQ4417";
        carte.SageNom = "Zorglub";
        carte.SagePrenom = "Ulysse";
        carte.Departement = "SERVICE-SECRET";
        var journal = new JournalMemoire<AbsenceReportService>();
        var service = new AbsenceReportService(ctx.Tenant, ctx.Loader, journal);
        var filtre = ctx.Filtre("MZQ4417", "MZQ4417", "SERVICE-SECRET");

        // Act
        await service.AbsencesJustifieesPagedAsync(filtre, 1, 50);
        await service.AbsencesAsync(filtre);

        // Assert
        journal.Messages.Should().HaveCount(2);
        var ligne = journal.Messages[0];
        ligne.Should().StartWith("[Rapports] type=absences-justifiees mode=pagine periode=");
        ligne.Should().Contain("filtres=matricules+departement");
        foreach (var champ in new[]
        {
            "chargement=", "(cartes=", "feries=", "conges=", "corrections=", "plannings=", "autres=", "filtres_sql=True",
            "calcul=", "impression=", "pagination=", "total=", "lues=", "produites="
        })
            ligne.Should().Contain(champ);
        journal.Messages[1].Should().StartWith("[Rapports] type=absences mode=liste");
        foreach (var message in journal.Messages)
        {
            message.Should().NotContain("MZQ4417");
            message.Should().NotContain("Zorglub");
            message.Should().NotContain("Ulysse");
            message.Should().NotContain("SERVICE-SECRET");
        }
    }
}
