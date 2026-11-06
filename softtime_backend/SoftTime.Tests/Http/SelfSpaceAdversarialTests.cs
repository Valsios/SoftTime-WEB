using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SoftTime.Application.SelfService;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;

namespace SoftTime.Tests.Http;

/// <summary>
/// Revue adverse de « Mon espace » : chaque test tente de casser la garde. Les tests « Limite connue » DÉMONTRENT un comportement
/// réel et indésirable qui n'est pas corrigé ici (la correction minimale proposée figure dans le rapport de revue) : s'ils échouent
/// un jour, c'est que la limite a été levée et le test doit alors être inversé.
/// </summary>
public class SelfSpaceAdversarialTests : IClassFixture<ApiFactory>
{
    private const string MatA = "A100";
    private const string MatB = "B200";
    private const decimal CompteId = 10401;       // porteur du jeton de A
    private const decimal DoublonId = 10402;      // second compte, même matricule que A
    private const decimal HybrideId = 10403;      // porte 1 + SELF:READ, une base
    private const decimal HybrideDeuxBasesId = 10404;

    private readonly ApiFactory _f;
    private readonly HttpClient _http;
    private readonly DateTime _aujourdhui = DateTime.Today;

    public SelfSpaceAdversarialTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        lock (factory.Store)
            Seed(factory.Store);
    }

    private static DateTime LundiDe(DateTime j) => j.Date.AddDays(-(((int)j.DayOfWeek + 6) % 7));
    private string Periode => $"from={_aujourdhui.AddDays(-45):yyyy-MM-dd}&to={_aujourdhui:yyyy-MM-dd}";

    private void Seed(SnapshotInMemoryUnitOfWork store)
    {
        var users = store.GetRepo<T_RESPONSABLE>().Items;
        if (users.Any(u => u.IDRESPONSABLE == CompteId))
            return;
        store.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 92, NOMROLE = "Salarié" });
        store.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { ID = 9201, IDROLE = 92, PERMISSION_CODE = "SELF:READ", IS_GRANTED = true });
        store.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 94, NOMROLE = "Hybride" });
        foreach (var (id, code) in new[] { (9401, "SELF:READ"), (9402, $"SAGE:{ApiFactory.SageName}:READ"), (9403, "POINTEUSE:PTG:READ") })
            store.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { ID = id, IDROLE = 94, PERMISSION_CODE = code, IS_GRANTED = true });

        users.Add(new T_RESPONSABLE { IDRESPONSABLE = CompteId, IDROLE = 92, LOGIN = "adv-1", NOMRESPONSABLE = "x", MATRICULE = MatA });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = DoublonId, IDROLE = 92, LOGIN = "adv-doublon", NOMRESPONSABLE = "x", MATRICULE = MatA });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = HybrideId, IDROLE = 94, LOGIN = "adv-hybride", NOMRESPONSABLE = "x", MATRICULE = MatA });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = HybrideDeuxBasesId, IDROLE = 94, LOGIN = "adv-hybride2", NOMRESPONSABLE = "x", MATRICULE = MatA });
        var acces = store.GetRepo<T_BDD_AUTORISE>().Items;
        foreach (var (id, idBase, n) in new[] { (CompteId, 3033, 1), (DoublonId, 3033, 2), (HybrideId, 3033, 3), (HybrideDeuxBasesId, 3033, 4), (HybrideDeuxBasesId, 3034, 5) })
            acces.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = 9400 + n, IDRESPONSABLE = id, IDBDD = idBase });

        var cartes = store.GetRepo<T_CARDPAIE>().Items;
        cartes.Add(new T_CARDPAIE { ID = 7001, SAGE_MATRICULE = MatA, SAGE_BDD = ApiFactory.SageName, POINTEUSE_BDD = "PTG" });
        cartes.Add(new T_CARDPAIE { ID = 7002, SAGE_MATRICULE = MatB, SAGE_BDD = ApiFactory.SageName, POINTEUSE_BDD = "PTG" });
        var hs = store.GetRepo<T_HS_VALIDER>().Items;
        hs.Add(new T_HS_VALIDER { ID = 1, BDD_SAGE = ApiFactory.SageName, MATRICULES = MatA, DATE_DEB = LundiDe(_aujourdhui.AddDays(-21)), HT = 40m, HS = 10m, VALIDATION_HS = true });
        hs.Add(new T_HS_VALIDER { ID = 2, BDD_SAGE = ApiFactory.SageName, MATRICULES = MatB, DATE_DEB = LundiDe(_aujourdhui.AddDays(-28)), HT = 50m, HS = 25m, VALIDATION_HS = true });
    }

    private static string Jeton(decimal userId, decimal roleId, string matricule, string cle = ApiFactory.TestKey, params int[] portes)
    {
        var uid = ((long)userId).ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, uid), new(ClaimTypes.NameIdentifier, uid), new("uid", uid), new(ClaimTypes.Name, $"t{uid}"),
            new("roleId", ((long)roleId).ToString()), new("matricule", matricule), new("rights", string.Join(",", portes))
        };
        var token = new JwtSecurityToken(ApiFactory.TestIssuer, ApiFactory.TestAudience, claims, expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cle)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private WebApplicationFactory<Program> Actif()
        => _f.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("SelfService:Enabled", "true") })));

    private static async Task<(HttpResponseMessage Reponse, string Texte)> Lire(HttpClient http, string url, string? jeton, string? base_ = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (jeton is not null)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {jeton}");
        if (base_ is not null)
            req.Headers.TryAddWithoutValidation("X-Sage-Database", base_);
        var r = await http.SendAsync(req);
        return (r, await r.Content.ReadAsStringAsync());
    }

    private static decimal HeuresSup(string texte)
    {
        using var j = JsonDocument.Parse(texte);
        return j.RootElement.GetProperty("semaines")[0].GetProperty("heuresSupplementaires").GetDecimal();
    }

    // ---------------------------------------------------------------- jeton falsifié

    [Fact]
    public async Task A_token_signed_with_another_key_or_unsigned_is_refused_whatever_matricule_it_claims()
    {
        using var f = Actif();
        var http = f.CreateClient();
        var url = $"/api/me/space/weeks?{Periode}";

        var mauvaiseCle = Jeton(CompteId, 92, MatB, "une-autre-cle-de-signature-0123456789-abcdefghijklmnop");
        var sansSignature = mauvaiseCle[..mauvaiseCle.LastIndexOf('.')] + ".";

        Assert.Equal(HttpStatusCode.Unauthorized, (await Lire(http, url, mauvaiseCle)).Reponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Lire(http, url, sansSignature)).Reponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Lire(http, url, "n-importe-quoi")).Reponse.StatusCode);
    }

    // ---------------------------------------------------------------- comptes qui cumulent les droits

    [Fact]
    public async Task A_gate_1_account_with_SELF_READ_and_one_base_reads_only_its_own_data_and_a_forged_other_base_is_refused()
    {
        using var f = Actif();
        var http = f.CreateClient();
        var jeton = Jeton(HybrideId, 94, MatA, portes: 1);

        var (confirmee, texte) = await Lire(http, $"/api/me/space/weeks?{Periode}", jeton, ApiFactory.SageName);
        var (autre, _) = await Lire(http, $"/api/me/space/weeks?{Periode}", jeton, ApiFactory.OtherSageName);

        Assert.Equal(HttpStatusCode.OK, confirmee.StatusCode);       // l'en-tête ne fait que confirmer l'unique base autorisée
        Assert.Equal(10m, HeuresSup(texte));
        Assert.Equal(HttpStatusCode.Forbidden, autre.StatusCode);    // base non autorisée forgée : refusée avant la garde
    }

    [Fact]
    public async Task A_gate_1_account_with_SELF_READ_and_two_authorized_bases_is_refused_with_an_explicit_message()
    {
        using var f = Actif();
        var (reponse, texte) = await Lire(f.CreateClient(), $"/api/me/space/weeks?{Periode}", Jeton(HybrideDeuxBasesId, 94, MatA, portes: 1), ApiFactory.SageName);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Contains("exactement une base", texte);
    }

    // ---------------------------------------------------------------- période hors limites (additionnel aux tests existants)

    [Fact]
    public void Period_boundaries_are_exact()
    {
        var aujourdhui = new DateTime(2026, 11, 30);
        var debutFenetre = new DateTime(2026, 8, 1);

        Assert.Equal((debutFenetre, aujourdhui), SelfSpaceService.Borner(debutFenetre, aujourdhui, aujourdhui));        // 4 mois exactement
        Assert.Throws<InvalidOperationException>(() => SelfSpaceService.Borner(debutFenetre.AddDays(-1), aujourdhui, aujourdhui));   // un jour avant la fenêtre
        Assert.Equal((aujourdhui, aujourdhui), SelfSpaceService.Borner(aujourdhui, aujourdhui.AddYears(10), aujourdhui));            // fin lointaine : plafonnée à aujourd'hui
        Assert.Throws<InvalidOperationException>(() => SelfSpaceService.Borner(aujourdhui.AddDays(1), aujourdhui.AddDays(2), aujourdhui));   // début futur
        Assert.Throws<InvalidOperationException>(() => SelfSpaceService.Borner(aujourdhui, aujourdhui.AddDays(-1), aujourdhui));     // fin avant début
        Assert.Throws<InvalidOperationException>(() => SelfSpaceService.Borner(DateTime.MinValue, aujourdhui, aujourdhui));          // année 1
        Assert.Equal((new DateTime(2026, 11, 1), aujourdhui), SelfSpaceService.Borner(null, null, aujourdhui));                      // défaut : mois en cours
    }

    [Theory]
    [InlineData("from=pas-une-date")]
    [InlineData("from=2026-13-45")]
    [InlineData("to=%27%3BDROP%20TABLE%20T_HS_VALIDER--")]
    public async Task A_malformed_date_is_a_400_never_a_500_and_never_data(string requete)
    {
        using var f = Actif();
        var (reponse, texte) = await Lire(f.CreateClient(), $"/api/me/space/weeks?{requete}", Jeton(CompteId, 92, MatA));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.DoesNotContain("semaines", texte);
    }

    // ---------------------------------------------------------------- en-têtes de réponse

    [Fact]
    public async Task Limite_connue_personal_responses_do_not_forbid_caching()
    {
        // Les réponses de Mon espace sont personnelles mais n'envoient pas « Cache-Control: no-store » : un navigateur partagé ou un
        // mandataire pourrait les conserver. Correction minimale proposée : [ResponseCache(NoStore = true, Location = None)] sur SelfSpaceController.
        using var f = Actif();
        var (reponse, _) = await Lire(f.CreateClient(), $"/api/me/space/weeks?{Periode}", Jeton(CompteId, 92, MatA));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.False(reponse.Headers.CacheControl?.NoStore ?? false);
    }

    // ---------------------------------------------------------------- limites connues démontrées

    [Fact]
    public async Task Limite_connue_a_token_keeps_its_matricule_for_12_hours_after_the_account_row_changes()
    {
        // Le matricule vient du jeton (voulu), mais le jeton dure 12 h : changer ou vider MATRICULE sur le compte ne s'applique qu'à la
        // reconnexion. Correction minimale proposée : la garde compare le claim au MATRICULE du compte (une lecture) et refuse s'ils diffèrent.
        using var f = Actif();
        var http = f.CreateClient();
        var jeton = Jeton(CompteId, 92, MatA);
        var (_, avant) = await Lire(http, $"/api/me/space/weeks?{Periode}", jeton);
        T_RESPONSABLE compte;
        lock (_f.Store)
            compte = _f.Store.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == CompteId);
        var ancien = compte.MATRICULE;
        try
        {
            compte.MATRICULE = MatB;                                   // la RH réaffecte le compte à un autre salarié
            var (reponse, apres) = await Lire(http, $"/api/me/space/weeks?{Periode}", jeton);

            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
            Assert.Equal(HeuresSup(avant), HeuresSup(apres));          // l'ancien jeton lit toujours les données de A
            Assert.Equal(10m, HeuresSup(apres));
        }
        finally
        {
            compte.MATRICULE = ancien;
        }
    }

    [Fact]
    public async Task Limite_connue_two_accounts_with_the_same_matricule_read_the_same_data()
    {
        // Aucune unicité du matricule sur T_RESPONSABLE : deux comptes partageant un matricule voient les mêmes données.
        // Correction minimale proposée : refuser (403) dans la garde si plusieurs comptes portent la même clé de matricule.
        using var f = Actif();
        var http = f.CreateClient();

        var (_, premier) = await Lire(http, $"/api/me/space/weeks?{Periode}", Jeton(CompteId, 92, MatA));
        var (_, second) = await Lire(http, $"/api/me/space/weeks?{Periode}", Jeton(DoublonId, 92, MatA));

        Assert.Equal(premier, second);
        Assert.Equal(10m, HeuresSup(second));
    }

    [Fact]
    public async Task Limite_connue_cold_leave_loads_are_not_shared_between_users()
    {
        // Charger les congés SAGE lit tout le tenant (plusieurs secondes sur la démo). Le cache est à la clé de chaque salarié : N salariés
        // qui se connectent ensemble déclenchent N lectures complètes. Correction minimale proposée : partager la LECTURE EN COURS entre
        // utilisateurs (une seule lecture du tenant, chacun n'y prenant que sa clé) sans conserver le dictionnaire du tenant.
        var cache = new SelfLeaveCache();
        var horloge = TimeProvider.System;
        var lectures = 0;
        Func<Task<List<LeaveDay>>> lecture = async () =>
        {
            Interlocked.Increment(ref lectures);
            await Task.Delay(50);
            return new List<LeaveDay>();
        };

        await Task.WhenAll(Enumerable.Range(1, 20).Select(i => cache.ObtenirAsync(SelfLeaveCache.Cle(i, "SAGE", "K" + i), horloge, lecture)));
        Assert.Equal(20, lectures);                                    // 20 salariés différents : 20 lectures du tenant

        lectures = 0;
        await Task.WhenAll(Enumerable.Range(1, 20).Select(_ => cache.ObtenirAsync(SelfLeaveCache.Cle(1, "SAGE", "K1"), horloge, lecture)));
        Assert.Equal(0, lectures);                                     // le même salarié : servi par le cache (déjà chargé)
    }

    [Fact]
    public async Task The_leave_cache_never_serves_an_entry_to_another_user_or_another_matricule()
    {
        var cache = new SelfLeaveCache();
        var horloge = TimeProvider.System;
        var jour = new LeaveDay(new DateTime(2026, 11, 3), 1m, "CP");

        var pourA = await cache.ObtenirAsync(SelfLeaveCache.Cle(1, "SAGE", "A100"), horloge, () => Task.FromResult(new List<LeaveDay> { jour }));
        var pourB = await cache.ObtenirAsync(SelfLeaveCache.Cle(2, "SAGE", "B200"), horloge, () => Task.FromResult(new List<LeaveDay>()));
        var memeUtilisateurAutreMatricule = await cache.ObtenirAsync(SelfLeaveCache.Cle(1, "SAGE", "B200"), horloge, () => Task.FromResult(new List<LeaveDay>()));
        var autreBase = await cache.ObtenirAsync(SelfLeaveCache.Cle(1, "AUTRE", "A100"), horloge, () => Task.FromResult(new List<LeaveDay>()));

        Assert.Single(pourA);
        Assert.Empty(pourB);
        Assert.Empty(memeUtilisateurAutreMatricule);
        Assert.Empty(autreBase);
    }
}
