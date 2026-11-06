using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

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
        public InMemoryUnitOfWork Uow { get; } = new();
        public Horloge Horloge { get; } = new();
        public ClosingService Service { get; }
        private int _planId;

        public Contexte()
        {
            Uow.Get<T_BDD_SAGE>().Items.Add(new T_BDD_SAGE { ID = 1, NOM_BD = "SAGE_TEST" });
            Uow.Get<T_CLOCK>().Items.Add(new T_CLOCK { IDPOINT = 1, MULTIPOINT = false });
            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(),
                SoftTime.Tests.Http.TestRights.Service(Uow), NullLogger<TenantConnectionService>.Instance);
            // Réglages propres à ce contexte : le cache de la checklist est donc isolé de celui des autres tests.
            Service = new ClosingService(Uow, tenant, new SageLeaveCalendar(tenant, new FakeExternalSourceService()), Horloge, new ClosingOptions());
        }

        public void Carte(int id, string matricule, string? departement = "INFO", string pointeuse = "PTE_TEST")
            => Uow.Get<T_CARDPAIE>().Items.Add(new T_CARDPAIE
            {
                ID = id, SAGE_MATRICULE = matricule, DEPARTEMENT = departement, SAGE_BDD = "SAGE_TEST", POINTEUSE_BDD = pointeuse
            });

        public void Plan(int idsal, DateTime jour, bool off = false)
            => Uow.Get<T_PLANNING_SAL>().Items.Add(new T_PLANNING_SAL
            {
                ID = ++_planId, IDSAL = idsal, DateP = jour.Date, HA = jour.Date.Add(H8), HD = jour.Date.Add(H17),
                OffP = off, BDD_SAGE = "SAGE_TEST", BDD_POINTEUSE = "PTE_TEST"
            });

        public void Semaine(int idsal, DateTime lundi)
        {
            for (var i = 0; i < 5; i++)
                Plan(idsal, lundi.AddDays(i));
        }

        public void Punch(string matricule, DateTime jour, string base_ = "SAGE_TEST", string pointeuse = "PTE_TEST")
            => Uow.Get<T_POINTAGE>().Items.Add(new T_POINTAGE
            {
                MATRICULE_SAGE = matricule, DATE_POINTAGE = jour.Date, HEURE_POINTAGE = H8,
                NOM_BDD_SAGE = base_, NOM_BDD_POINTEUSE = pointeuse
            });

        public void Anomalie(string matricule, DateTime jour)
            => Uow.Get<T_HEUREANOMALIE>().Items.Add(new T_HEUREANOMALIE { MATRICULE_SAGE = matricule, DATE_POINTAGE_IN = jour.Date, BDD_SAGE = "SAGE_TEST" });

        public void Correction(string matricule, DateTime jour)
            => Uow.Get<T_HEURECORRIGER>().Items.Add(new T_HEURECORRIGER
            {
                MATRICULE_SAGE = matricule, DATE_POINTAGE_IN = jour.Date, HENTREE = H8, HSORTIE = H17, BDD_SAGE = "SAGE_TEST"
            });

        public void Validee(string matricule, DateTime lundi, bool validee = true)
            => Uow.Get<T_HS_VALIDER>().Items.Add(new T_HS_VALIDER
            {
                BDD_SAGE = "SAGE_TEST", MATRICULES = matricule, DATE_DEB = lundi, DATE_FIN = lundi.AddDays(6), VALIDATION_HS = validee
            });

        public void Exo(string matricule, DateTime debut, DateTime fin, decimal impo = 0m)
            => Uow.Get<T_HSExoImp>().Items.Add(new T_HSExoImp
            {
                Matricule = matricule, PeriodeDebut = debut, PeriodeFin = fin, EXO130 = 8m, I130 = impo, BDD_SAGE = "SAGE_TEST"
            });

        public void Synchro(string statut, DateTime debut, DateTime fin, DateTime quand)
            => Uow.Get<suivi_JOURNAL>().Items.Add(new suivi_JOURNAL
            {
                DATE_UTC = quand, TABLES = "T_CUMSAL", ACTIONS = "SYNC_PAIE", USERS = "test", SOURCE = "test",
                BASE_SAGE = "SAGE_TEST",
                NOUVEAUX = $"Statut={statut}; Période={debut:yyyy-MM-dd}..{fin:yyyy-MM-dd}; Salariés=2"
            });

        public async Task<IReadOnlyDictionary<string, ClosingChecklistLineDto>> Lignes(string? departement = null, bool reuse = false, string perimetre = "u1")
        {
            var c = await Service.ChecklistAsync(new ClosingChecklistRequest(Debut, Fin, departement, reuse), perimetre);
            return c.Lignes.ToDictionary(l => l.Code);
        }
    }

    // ----------------------------------------------------------------------------- absence de données

    [Fact]
    public async Task NoData_EveryLineIsExplicit_NothingIsGuessed()
    {
        var ctx = new Contexte();

        var l = await ctx.Lignes();

        Assert.Equal(ClosingService.EtatAFaire, l["Pointages"].Etat);
        Assert.Equal(ClosingService.EtatFait, l["Anomalies"].Etat);               // aucune anomalie, aucun pointage en attente de traitement
        Assert.Equal(ClosingService.EtatInformation, l["Semaines"].Etat);         // rien de planifié à valider
        Assert.Equal(ClosingService.EtatAFaire, l["ExoImpo"].Etat);
        Assert.False(l.ContainsKey("HeuresImposables"));                           // seulement si le calcul existe
        Assert.Equal(ClosingService.EtatNonDeterminable, l["SynchroPaie"].Etat);   // aucune trace stockée
    }

    [Fact]
    public async Task Response_CarriesOnlyCountsAndLabels_NoMatriculeNorName()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "A777");
        ctx.Punch("A777", Debut);
        ctx.Anomalie("A777", Debut);

        var json = JsonSerializer.Serialize(await ctx.Service.ChecklistAsync(new ClosingChecklistRequest(Debut, Fin), "u1"));

        Assert.DoesNotContain("A777", json);
        Assert.DoesNotContain("777", json);
    }

    // ----------------------------------------------------------------------------- a) pointages, b) anomalies

    [Fact]
    public async Task Punches_CountOnlyThePerimeterBasePeriodAndPointeuse()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "001", "INFO");
        ctx.Carte(2, "002", "RH");
        ctx.Carte(3, "003", "INFO", pointeuse: "AUTRE_PTE");
        ctx.Punch("001", Debut);
        ctx.Punch("001", Debut.AddDays(1));
        ctx.Punch("002", Debut);
        ctx.Punch("003", Debut);                                  // carte d'une autre pointeuse
        ctx.Punch("001", Debut.AddDays(-1));                      // hors période
        ctx.Punch("001", Debut, base_: "AUTRE_BASE");             // autre base SAGE

        var tous = await ctx.Lignes();
        var info = await ctx.Lignes("INFO");

        Assert.Equal(ClosingService.EtatFait, tous["Pointages"].Etat);
        Assert.Equal(3, tous["Pointages"].Nombre);
        Assert.Equal(2, info["Pointages"].Nombre);
    }

    [Fact]
    public async Task Anomalies_RemainingAnomaliesAreToDo_AndZeroWithoutProcessingIsToDoToo()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Punch("001", Debut);

        var nonTraites = await ctx.Lignes();                      // pointages sans correction ni anomalie : traitement non lancé
        Assert.Equal(ClosingService.EtatAFaire, nonTraites["Anomalies"].Etat);
        Assert.Contains("n'ont pas été traités", nonTraites["Anomalies"].Detail);

        ctx.Correction("001", Debut);
        Assert.Equal(ClosingService.EtatFait, (await ctx.Lignes())["Anomalies"].Etat);

        ctx.Anomalie("001", Debut.AddDays(1));
        ctx.Anomalie("001", Debut.AddDays(2));
        ctx.Anomalie("001", Debut.AddDays(-30));                  // hors période : ignorée
        var restantes = (await ctx.Lignes())["Anomalies"];
        Assert.Equal(ClosingService.EtatAFaire, restantes.Etat);
        Assert.Equal(2, restantes.Nombre);
    }

    // ----------------------------------------------------------------------------- c) semaines validées

    [Fact]
    public async Task Weeks_ArePairedByTheSundayRule_AndOnlyValidatedOnesCount()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Carte(2, "002");
        ctx.Semaine(1, Debut);                                    // lundi 17 : dimanche 23, dans la période
        ctx.Semaine(2, Debut);
        ctx.Semaine(1, Debut.AddDays(-7));                        // lundi 10 : dimanche 16, avant la période : non attendue
        ctx.Plan(1, Debut.AddDays(-3));                           // samedi 14 de la semaine du 10 : même exclusion

        var aucune = (await ctx.Lignes())["Semaines"];
        Assert.Equal(ClosingService.EtatAFaire, aucune.Etat);
        Assert.Equal(2, aucune.Nombre);

        ctx.Validee("001", Debut);
        ctx.Validee("002", Debut, validee: false);                // ligne non validée : ne compte pas
        var une = (await ctx.Lignes())["Semaines"];
        Assert.Equal(ClosingService.EtatAFaire, une.Etat);
        Assert.Equal(1, une.Nombre);
        Assert.Contains("1 semaine(s)-salarié validée(s) sur 2", une.Detail);

        ctx.Validee("002", Debut);
        Assert.Equal(ClosingService.EtatFait, (await ctx.Lignes())["Semaines"].Etat);
    }

    [Fact]
    public async Task Weeks_OffDaysAreNotExpected_AndTheDepartmentFilterApplies()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "001", "INFO");
        ctx.Carte(2, "002", "RH");
        ctx.Plan(1, Debut, off: true);                            // jour OFF seulement : rien à valider pour 001
        ctx.Semaine(2, Debut);

        var tous = (await ctx.Lignes())["Semaines"];
        var info = (await ctx.Lignes("INFO"))["Semaines"];

        Assert.Equal(1, tous.Nombre);                             // seul 002 a une semaine travaillée
        Assert.Equal(ClosingService.EtatInformation, info.Etat);
    }

    // ----------------------------------------------------------------------------- d) EXO/IMPO, e) heures imposables

    [Fact]
    public async Task ExoImpo_ExactPeriodIsDone_AndImposableHoursAreAnInformationLine()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Carte(2, "002");
        ctx.Exo("001", Debut, Fin, impo: 3m);
        ctx.Exo("002", Debut, Fin, impo: 0m);

        var l = await ctx.Lignes();

        Assert.Equal(ClosingService.EtatFait, l["ExoImpo"].Etat);
        Assert.Equal(2, l["ExoImpo"].Nombre);
        Assert.Equal(ClosingService.EtatInformation, l["HeuresImposables"].Etat);
        Assert.Equal(1, l["HeuresImposables"].Nombre);
        Assert.Contains("20", l["HeuresImposables"].Detail);      // plafond OvertimeRules.ExemptCeilingHours
    }

    [Fact]
    public async Task ExoImpo_OtherOverlappingPeriodIsNotDeterminable_NoImposableLine()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Exo("001", Debut.AddDays(2), Fin);

        var l = await ctx.Lignes();

        Assert.Equal(ClosingService.EtatNonDeterminable, l["ExoImpo"].Etat);
        Assert.False(l.ContainsKey("HeuresImposables"));
    }

    // ----------------------------------------------------------------------------- f) synchronisation paie

    [Fact]
    public async Task PayrollSync_ReadsTheLatestJournalEntryOfTheSamePeriodOnly()
    {
        var ctx = new Contexte();
        ctx.Synchro("SUCCES", Debut.AddDays(-14), Debut.AddDays(-1), new DateTime(2025, 12, 1));   // autre période : ignorée
        Assert.Equal(ClosingService.EtatNonDeterminable, (await ctx.Lignes())["SynchroPaie"].Etat);

        ctx.Synchro("SUCCES", Debut, Fin, new DateTime(2025, 12, 2));
        Assert.Equal(ClosingService.EtatFait, (await ctx.Lignes())["SynchroPaie"].Etat);

        ctx.Synchro("ECHEC", Debut, Fin, new DateTime(2025, 12, 3));                                // la plus récente l'emporte
        Assert.Equal(ClosingService.EtatAFaire, (await ctx.Lignes())["SynchroPaie"].Etat);
    }

    // ----------------------------------------------------------------------------- période, cache

    [Fact]
    public async Task InvalidPeriod_IsRefused()
    {
        var ctx = new Contexte();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Service.ChecklistAsync(new ClosingChecklistRequest(Fin, Debut), "u1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Service.ChecklistAsync(new ClosingChecklistRequest(Debut, default), "u1"));
    }

    [Fact]
    public async Task Cache_IsReusedOnlyOnRequest_ThenExpires_AndIsKeyedByUserAndDepartment()
    {
        var ctx = new Contexte();
        ctx.Carte(1, "001");
        ctx.Punch("001", Debut);
        Assert.Equal(1, (await ctx.Lignes())["Pointages"].Nombre);        // remplit le cache

        ctx.Punch("001", Debut.AddDays(1));                              // la donnée change
        Assert.Equal(1, (await ctx.Lignes(reuse: true))["Pointages"].Nombre);      // dans les 60 s : conservée
        Assert.Equal(2, (await ctx.Lignes(reuse: true, perimetre: "u2"))["Pointages"].Nombre);   // autre utilisateur : jamais l'entrée d'un autre
        Assert.Equal(2, (await ctx.Lignes("INFO", reuse: true))["Pointages"].Nombre);            // autre département : autre clé
        Assert.Equal(2, (await ctx.Lignes())["Pointages"].Nombre);       // sans reuse : toujours recalculée, et remplace l'entrée

        ctx.Punch("001", Debut.AddDays(2));
        ctx.Horloge.Avancer(TimeSpan.FromSeconds(61));
        Assert.Equal(3, (await ctx.Lignes(reuse: true))["Pointages"].Nombre);      // expirée : recalculée
    }

    // ----------------------------------------------------------------------------- l'analyse ne change pas

    [Fact]
    public async Task Analysis_IsStrictlyIdenticalWithOrWithoutTheChecklistData()
    {
        string Json(object o) => JsonSerializer.Serialize(o);

        var seul = new Contexte();
        var avec = new Contexte();
        foreach (var ctx in new[] { seul, avec })
        {
            ctx.Carte(1, "001");
            ctx.Semaine(1, Debut);
            ctx.Punch("001", Debut);
        }
        // Données propres à la checklist, présentes d'un seul côté (l'analyse ne les lit pas : les semaines validées,
        // que les règles de clôture lisent déjà, sont volontairement absentes de ce jeu).
        avec.Exo("001", Debut, Fin, impo: 2m);
        avec.Synchro("SUCCES", Debut, Fin, new DateTime(2025, 12, 2));
        avec.Anomalie("001", Debut);

        var demande = new ClosingAnalysisRequest(Debut, Fin, null, 1, 50, IncludeComparison: true, IncludeInformation: true);
        var avant = await seul.Service.AnalyserAsync(demande);
        var avantSurLeMemeContexte = await avec.Service.AnalyserAsync(demande);
        await avec.Service.ChecklistAsync(new ClosingChecklistRequest(Debut, Fin), "u1");
        var apres = await avec.Service.AnalyserAsync(demande);

        Assert.Equal(Json(avantSurLeMemeContexte), Json(apres));            // l'appel de la checklist ne change rien
        Assert.Equal(Json(avant), Json(apres));                             // et ses données propres non plus
        Assert.Equal(Json(avant.Score), Json(apres.Score));
        Assert.Equal(avant.Statut, apres.Statut);
        Assert.Equal(Json(avant.ParGravite), Json(apres.ParGravite));
        Assert.Equal(Json(avant.ParType), Json(apres.ParType));
        Assert.Equal(Json(avant.Anomalies), Json(apres.Anomalies));
        Assert.Equal(avant.NombreAnomalies, apres.NombreAnomalies);
    }
}
