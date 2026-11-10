using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.RequestContext;

namespace SoftTime.Tests;

/// <summary>
/// Checklist d'avancement de la paie : une ligne par étape, uniquement à partir de données et de règles existantes,
/// sur des jeux fictifs (aucune donnée de salarié). Elle ne modifie jamais la réponse de l'analyse.
/// </summary>
public class ClosingChecklistTests
{
    private static readonly DateTime Today = new(2025, 12, 5);
    private static readonly DateTime Debut = new(2025, 11, 17);   // lundi
    private static readonly DateTime Fin = new(2025, 11, 30);     // dimanche : contient les dimanches du 23 et du 30
    private static readonly TimeSpan H8 = TimeSpan.FromHours(8);
    private static readonly TimeSpan H17 = TimeSpan.FromHours(17);

    private sealed class Horloge : TimeProvider
    {
        private DateTimeOffset _maintenant = new(Today.AddHours(10), TimeSpan.Zero);
        public void Avancer(TimeSpan d) => _maintenant += d;
        public override DateTimeOffset GetUtcNow() => _maintenant;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Contexte
    {
        private int _planId;

        public Contexte()
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { IdPoint = 1, MultiPoint = false });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(),
                SoftTime.Tests.Http.TestRights.Service(Uow), NullLogger<TenantConnectionService>.Instance);
            var reglages = Options.Create(new ClosingOptions());
            // Cache propre à ce contexte : isolé de celui des autres tests.
            Sut = new ClosingChecklistService(Uow, tenant, new ClosingChecklistCache(), reglages, Horloge,
                NullLogger<ClosingChecklistService>.Instance);
            Analyse = new ClosingService(
                Uow, tenant, new SageLeaveCalendar(tenant, new FakeExternalSourceService()), Horloge, reglages);
        }

        public InMemoryUnitOfWork Uow { get; } = new();
        public Horloge Horloge { get; } = new();
        public ClosingChecklistService Sut { get; }
        public ClosingService Analyse { get; }

        public void Carte(int id, string matricule, string? departement = "INFO", string pointeuse = "PTE_TEST")
            => Uow.Get<CardPaie>().Items.Add(new CardPaie
            {
                Id = id, SageMatricule = matricule, Departement = departement, SageBdd = "SAGE_TEST", PointeuseBdd = pointeuse
            });

        public void Plan(int idsal, DateTime jour, bool off = false)
            => Uow.Get<PlanningSalarie>().Items.Add(new PlanningSalarie
            {
                Id = ++_planId, IdSal = idsal, DateP = jour.Date, Ha = jour.Date.Add(H8), Hd = jour.Date.Add(H17),
                OffP = off, BddSage = "SAGE_TEST", BddPointeuse = "PTE_TEST"
            });

        public void Semaine(int idsal, DateTime lundi)
        {
            for (var i = 0; i < 5; i++)
                Plan(idsal, lundi.AddDays(i));
        }

        public void Punch(string matricule, DateTime jour, string baseSage = "SAGE_TEST", string pointeuse = "PTE_TEST")
            => Uow.Get<Pointage>().Items.Add(new Pointage
            {
                MatriculeSage = matricule, DatePointage = jour.Date, HeurePointage = H8,
                NomBddSage = baseSage, NomBddPointeuse = pointeuse
            });

        public void Anomalie(string matricule, DateTime jour)
            => Uow.Get<HeureAnomalie>().Items.Add(new HeureAnomalie
            {
                MatriculeSage = matricule, DatePointageIn = jour.Date, BddSage = "SAGE_TEST"
            });

        public void Correction(string matricule, DateTime jour)
            => Uow.Get<HeureCorrigee>().Items.Add(new HeureCorrigee
            {
                MatriculeSage = matricule, DatePointageIn = jour.Date, HEntree = H8, HSortie = H17, BddSage = "SAGE_TEST"
            });

        public void Validee(string matricule, DateTime lundi, bool validee = true)
            => Uow.Get<HsValidee>().Items.Add(new HsValidee
            {
                BddSage = "SAGE_TEST", Matricules = matricule, DateDeb = lundi, DateFin = lundi.AddDays(6), ValidationHs = validee
            });

        public void Exo(string matricule, DateTime debut, DateTime fin, decimal impo = 0m)
            => Uow.Get<HsExoImp>().Items.Add(new HsExoImp
            {
                Matricule = matricule, PeriodeDebut = debut, PeriodeFin = fin, Exo130 = 8m, I130 = impo, BddSage = "SAGE_TEST"
            });

        public void Synchro(string statut, (DateTime Debut, DateTime Fin) periode, DateTime quand)
            => Uow.Get<JournalAudit>().Items.Add(new JournalAudit
            {
                DateUtc = quand, Tables = "T_CUMSAL", Actions = "SYNC_PAIE", Users = "test", Source = "test",
                BaseSage = "SAGE_TEST",
                Nouveaux = $"Statut={statut}; Période={periode.Debut:yyyy-MM-dd}..{periode.Fin:yyyy-MM-dd}; Salariés=2"
            });

        public async Task<IReadOnlyDictionary<string, ClosingChecklistLineDto>> LignesAsync(
            string? departement = null, bool reuse = false, string perimetre = "u1")
        {
            var c = await Sut.ChecklistAsync(new ClosingChecklistRequest(Debut, Fin, departement, reuse, perimetre));
            return c.Lignes.ToDictionary(l => l.Code);
        }
    }

    // ----------------------------------------------------------------------------- absence de données

    [Fact]
    public async Task ChecklistAsync_NoData_EveryLineIsExplicitNothingIsGuessed()
    {
        // Arrange
        var ctx = new Contexte();

        // Act
        var l = await ctx.LignesAsync();

        // Assert
        l["Pointages"].Etat.Should().Be(ClosingChecklistLines.EtatAFaire);
        l["Anomalies"].Etat.Should().Be(ClosingChecklistLines.EtatFait);           // aucun pointage en attente de traitement
        l["Semaines"].Etat.Should().Be(ClosingChecklistLines.EtatInformation);     // rien de planifié à valider
        l["ExoImpo"].Etat.Should().Be(ClosingChecklistLines.EtatAFaire);
        l.Should().NotContainKey("HeuresImposables");                               // seulement si le calcul existe
        l["SynchroPaie"].Etat.Should().Be(ClosingChecklistLines.EtatNonDeterminable); // aucune trace stockée
    }

    [Fact]
    public async Task ChecklistAsync_AnyData_CarriesOnlyCountsAndLabels()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "A777");
        ctx.Punch("A777", Debut);
        ctx.Anomalie("A777", Debut);

        // Act
        var json = JsonSerializer.Serialize(await ctx.Sut.ChecklistAsync(new ClosingChecklistRequest(Debut, Fin)));

        // Assert
        json.Should().NotContain("A777");
        json.Should().NotContain("777");
    }

    // ----------------------------------------------------------------------------- a) pointages, b) anomalies

    [Fact]
    public async Task ChecklistAsync_Punches_CountOnlyPerimeterBasePeriodAndPointeuse()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001", "INFO");
        ctx.Carte(2, "002", "RH");
        ctx.Carte(3, "003", "INFO", pointeuse: "AUTRE_PTE");
        ctx.Punch("001", Debut);
        ctx.Punch("001", Debut.AddDays(1));
        ctx.Punch("002", Debut);
        ctx.Punch("003", Debut);                                  // carte d'une autre pointeuse
        ctx.Punch("001", Debut.AddDays(-1));                      // hors période
        ctx.Punch("001", Debut, baseSage: "AUTRE_BASE");          // autre base SAGE

        // Act
        var tous = await ctx.LignesAsync();
        var info = await ctx.LignesAsync("INFO");

        // Assert
        tous["Pointages"].Etat.Should().Be(ClosingChecklistLines.EtatFait);
        tous["Pointages"].Nombre.Should().Be(3);
        info["Pointages"].Nombre.Should().Be(2);
    }

    [Fact]
    public async Task ChecklistAsync_PunchesWithoutProcessing_AnomaliesAreToDo()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Punch("001", Debut);                                  // ni correction ni anomalie : traitement non lancé

        // Act
        var anomalies = (await ctx.LignesAsync())["Anomalies"];

        // Assert
        anomalies.Etat.Should().Be(ClosingChecklistLines.EtatAFaire);
        anomalies.Detail.Should().Contain("n'ont pas été traités");
    }

    [Fact]
    public async Task ChecklistAsync_ProcessedPunchesWithoutAnomaly_AnomaliesAreDone()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Punch("001", Debut);
        ctx.Correction("001", Debut);

        // Act
        var anomalies = (await ctx.LignesAsync())["Anomalies"];

        // Assert
        anomalies.Etat.Should().Be(ClosingChecklistLines.EtatFait);
    }

    [Fact]
    public async Task ChecklistAsync_RemainingAnomalies_AreToDoAndCountedInPeriodOnly()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Punch("001", Debut);
        ctx.Correction("001", Debut);
        ctx.Anomalie("001", Debut.AddDays(1));
        ctx.Anomalie("001", Debut.AddDays(2));
        ctx.Anomalie("001", Debut.AddDays(-30));                  // hors période : ignorée

        // Act
        var restantes = (await ctx.LignesAsync())["Anomalies"];

        // Assert
        restantes.Etat.Should().Be(ClosingChecklistLines.EtatAFaire);
        restantes.Nombre.Should().Be(2);
    }

    // ----------------------------------------------------------------------------- c) semaines validées

    [Fact]
    public async Task ChecklistAsync_NoValidatedWeek_WeeksPairedBySundayRuleAreToDo()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Carte(2, "002");
        ctx.Semaine(1, Debut);                                    // lundi 17 : dimanche 23, dans la période
        ctx.Semaine(2, Debut);
        ctx.Semaine(1, Debut.AddDays(-7));                        // lundi 10 : dimanche 16, avant la période
        ctx.Plan(1, Debut.AddDays(-3));                           // samedi 14 de la semaine du 10 : même exclusion

        // Act
        var semaines = (await ctx.LignesAsync())["Semaines"];

        // Assert
        semaines.Etat.Should().Be(ClosingChecklistLines.EtatAFaire);
        semaines.Nombre.Should().Be(2);
    }

    [Fact]
    public async Task ChecklistAsync_PartlyValidatedWeeks_OnlyValidatedOnesCount()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Carte(2, "002");
        ctx.Semaine(1, Debut);
        ctx.Semaine(2, Debut);
        ctx.Validee("001", Debut);
        ctx.Validee("002", Debut, validee: false);                // ligne non validée : ne compte pas

        // Act
        var semaines = (await ctx.LignesAsync())["Semaines"];

        // Assert
        semaines.Etat.Should().Be(ClosingChecklistLines.EtatAFaire);
        semaines.Nombre.Should().Be(1);
        semaines.Detail.Should().Contain("1 semaine(s)-salarié validée(s) sur 2");
    }

    [Fact]
    public async Task ChecklistAsync_AllWeeksValidated_WeeksAreDone()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Carte(2, "002");
        ctx.Semaine(1, Debut);
        ctx.Semaine(2, Debut);
        ctx.Validee("001", Debut);
        ctx.Validee("002", Debut);

        // Act
        var semaines = (await ctx.LignesAsync())["Semaines"];

        // Assert
        semaines.Etat.Should().Be(ClosingChecklistLines.EtatFait);
    }

    [Fact]
    public async Task ChecklistAsync_OffDaysAndDepartmentFilter_AreNotExpected()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001", "INFO");
        ctx.Carte(2, "002", "RH");
        ctx.Plan(1, Debut, off: true);                            // jour OFF seulement : rien à valider pour 001
        ctx.Semaine(2, Debut);

        // Act
        var tous = (await ctx.LignesAsync())["Semaines"];
        var info = (await ctx.LignesAsync("INFO"))["Semaines"];

        // Assert
        tous.Nombre.Should().Be(1);                               // seul 002 a une semaine travaillée
        info.Etat.Should().Be(ClosingChecklistLines.EtatInformation);
    }

    // ----------------------------------------------------------------------------- d) EXO/IMPO, e) heures imposables

    [Fact]
    public async Task ChecklistAsync_ExactExoImpoPeriod_IsDoneWithImposableHoursInformation()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Carte(2, "002");
        ctx.Exo("001", Debut, Fin, impo: 3m);
        ctx.Exo("002", Debut, Fin, impo: 0m);

        // Act
        var l = await ctx.LignesAsync();

        // Assert
        l["ExoImpo"].Etat.Should().Be(ClosingChecklistLines.EtatFait);
        l["ExoImpo"].Nombre.Should().Be(2);
        l["HeuresImposables"].Etat.Should().Be(ClosingChecklistLines.EtatInformation);
        l["HeuresImposables"].Nombre.Should().Be(1);
        l["HeuresImposables"].Detail.Should().Contain("20");      // plafond OvertimeRules.ExemptCeilingHours
    }

    [Fact]
    public async Task ChecklistAsync_OtherOverlappingExoImpoPeriod_IsNotDeterminable()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Exo("001", Debut.AddDays(2), Fin);

        // Act
        var l = await ctx.LignesAsync();

        // Assert
        l["ExoImpo"].Etat.Should().Be(ClosingChecklistLines.EtatNonDeterminable);
        l.Should().NotContainKey("HeuresImposables");
    }

    // ----------------------------------------------------------------------------- f) synchronisation paie

    [Fact]
    public async Task ChecklistAsync_SyncOfAnotherPeriodOnly_IsNotDeterminable()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Synchro("SUCCES", (Debut.AddDays(-14), Debut.AddDays(-1)), new DateTime(2025, 12, 1));

        // Act
        var synchro = (await ctx.LignesAsync())["SynchroPaie"];

        // Assert
        synchro.Etat.Should().Be(ClosingChecklistLines.EtatNonDeterminable);
    }

    [Fact]
    public async Task ChecklistAsync_SuccessfulSyncOfPeriod_IsDone()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Synchro("SUCCES", (Debut, Fin), new DateTime(2025, 12, 2));

        // Act
        var synchro = (await ctx.LignesAsync())["SynchroPaie"];

        // Assert
        synchro.Etat.Should().Be(ClosingChecklistLines.EtatFait);
    }

    [Fact]
    public async Task ChecklistAsync_LatestSyncFailed_IsToDo()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Synchro("SUCCES", (Debut, Fin), new DateTime(2025, 12, 2));
        ctx.Synchro("ECHEC", (Debut, Fin), new DateTime(2025, 12, 3));   // la plus récente l'emporte

        // Act
        var synchro = (await ctx.LignesAsync())["SynchroPaie"];

        // Assert
        synchro.Etat.Should().Be(ClosingChecklistLines.EtatAFaire);
    }

    // ----------------------------------------------------------------------------- période, cache

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChecklistAsync_InvalidPeriod_IsRefused(bool finAvantDebut)
    {
        // Arrange
        var ctx = new Contexte();
        var request = finAvantDebut ? new ClosingChecklistRequest(Fin, Debut) : new ClosingChecklistRequest(Debut, default);

        // Act
        var act = () => ctx.Sut.ChecklistAsync(request);

        // Assert
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task ChecklistAsync_Cache_IsReusedOnRequestAndKeyedByUserAndDepartment()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Punch("001", Debut);
        var initial = (await ctx.LignesAsync())["Pointages"].Nombre;            // remplit le cache
        ctx.Punch("001", Debut.AddDays(1));                                     // la donnée change

        // Act
        var conservee = (await ctx.LignesAsync(reuse: true))["Pointages"].Nombre;                    // dans les 60 s
        var autreUtilisateur = (await ctx.LignesAsync(reuse: true, perimetre: "u2"))["Pointages"].Nombre;
        var autreDepartement = (await ctx.LignesAsync("INFO", reuse: true))["Pointages"].Nombre;
        var sansReuse = (await ctx.LignesAsync())["Pointages"].Nombre;          // toujours recalculée

        // Assert
        initial.Should().Be(1);
        conservee.Should().Be(1);
        autreUtilisateur.Should().Be(2);                                        // jamais l'entrée d'un autre
        autreDepartement.Should().Be(2);                                        // autre clé
        sansReuse.Should().Be(2);
    }

    [Fact]
    public async Task ChecklistAsync_CacheAfter60Seconds_IsRecomputed()
    {
        // Arrange
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Punch("001", Debut);
        await ctx.LignesAsync();
        ctx.Punch("001", Debut.AddDays(1));
        ctx.Horloge.Avancer(TimeSpan.FromSeconds(61));

        // Act
        var nombre = (await ctx.LignesAsync(reuse: true))["Pointages"].Nombre;

        // Assert
        nombre.Should().Be(2);
    }

    // ----------------------------------------------------------------------------- l'analyse ne change pas

    [Fact]
    public async Task ChecklistAsync_ChecklistDataAndCall_LeaveAnalysisStrictlyIdentical()
    {
        // Arrange
        var seul = new Contexte();
        var avec = new Contexte();
        foreach (var ctx in new[] { seul, avec })
        {
            ctx.Carte(1, "001");
            ctx.Semaine(1, Debut);
            ctx.Punch("001", Debut);
        }
        // Données propres à la checklist, présentes d'un seul côté (les semaines validées, que les règles de clôture
        // lisent déjà, sont volontairement absentes de ce jeu).
        avec.Exo("001", Debut, Fin, impo: 2m);
        avec.Synchro("SUCCES", (Debut, Fin), new DateTime(2025, 12, 2));
        avec.Anomalie("001", Debut);
        var demande = new ClosingAnalysisRequest(Debut, Fin, null, 1, 50, IncludeComparison: true, IncludeInformation: true);
        var avant = await seul.Analyse.AnalyserAsync(demande);
        var avantSurLeMemeContexte = await avec.Analyse.AnalyserAsync(demande);

        // Act
        await avec.Sut.ChecklistAsync(new ClosingChecklistRequest(Debut, Fin));
        var apres = await avec.Analyse.AnalyserAsync(demande);

        // Assert
        JsonSerializer.Serialize(apres).Should().Be(JsonSerializer.Serialize(avantSurLeMemeContexte)); // l'appel ne change rien
        JsonSerializer.Serialize(apres).Should().Be(JsonSerializer.Serialize(avant));                  // ses données non plus
    }
}
