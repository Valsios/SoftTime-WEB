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
    private const decimal SupprimeId = 10405;         // compte supprimé en cours de test (matricule propre)

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
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = DoublonId, IDROLE = 92, LOGIN = "adv-doublon", NOMRESPONSABLE = "x", MATRICULE = "D400" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = HybrideId, IDROLE = 94, LOGIN = "adv-hybride", NOMRESPONSABLE = "x", MATRICULE = MatB });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = SupprimeId, IDROLE = 92, LOGIN = "adv-supprime", NOMRESPONSABLE = "x", MATRICULE = "S500" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = HybrideDeuxBasesId, IDROLE = 94, LOGIN = "adv-hybride2", NOMRESPONSABLE = "x", MATRICULE = "HB300" });
        var acces = store.GetRepo<T_BDD_AUTORISE>().Items;
        foreach (var (id, idBase, n) in new[] { (CompteId, 3033, 1), (DoublonId, 3033, 2), (HybrideId, 3033, 3), (HybrideDeuxBasesId, 3033, 4), (HybrideDeuxBasesId, 3034, 5), (SupprimeId, 3033, 6) })
            acces.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = 9400 + n, IDRESPONSABLE = id, IDBDD = idBase });

        var cartes = store.GetRepo<T_CARDPAIE>().Items;
        cartes.Add(new T_CARDPAIE { ID = 7001, SAGE_MATRICULE = MatA, SAGE_BDD = ApiFactory.SageName, POINTEUSE_BDD = "PTG" });
        cartes.Add(new T_CARDPAIE { ID = 7003, SAGE_MATRICULE = "S500", SAGE_BDD = ApiFactory.SageName, POINTEUSE_BDD = "PTG" });
        cartes.Add(new T_CARDPAIE { ID = 7004, SAGE_MATRICULE = "N300", SAGE_BDD = ApiFactory.SageName, POINTEUSE_BDD = "PTG" });
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
        var jeton = Jeton(HybrideId, 94, MatB, portes: 1);

        var (confirmee, texte) = await Lire(http, $"/api/me/space/weeks?{Periode}", jeton, ApiFactory.SageName);
        var (autre, _) = await Lire(http, $"/api/me/space/weeks?{Periode}", jeton, ApiFactory.OtherSageName);

        Assert.Equal(HttpStatusCode.OK, confirmee.StatusCode);       // l'en-tête ne fait que confirmer l'unique base autorisée
        Assert.Equal(25m, HeuresSup(texte));                         // les données de B, jamais celles de A
        Assert.Equal(HttpStatusCode.Forbidden, autre.StatusCode);    // base non autorisée forgée : refusée avant la garde
    }

    [Fact]
    public async Task A_gate_1_account_with_SELF_READ_and_two_authorized_bases_is_refused_with_an_explicit_message()
    {
        using var f = Actif();
        var (reponse, texte) = await Lire(f.CreateClient(), $"/api/me/space/weeks?{Periode}", Jeton(HybrideDeuxBasesId, 94, "HB300", portes: 1), ApiFactory.SageName);

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

    [Theory]
    [InlineData("summary")]
    [InlineData("weeks")]
    [InlineData("anomalies")]
    [InlineData("absences")]
    [InlineData("delays")]
    public async Task Personal_responses_forbid_caching_on_every_route_including_refusals(string route)
    {
        // Correction : [NoStore] sur SelfSpaceController. Chaque route répond Cache-Control: no-store et Pragma: no-cache
        // pour un succès, un refus 403, une erreur de saisie 400 et le 404 de la fonction désactivée.
        static void VerifierEntetes(HttpResponseMessage reponse, string cas)
        {
            Assert.True(reponse.Headers.CacheControl?.NoStore ?? false, $"{cas} : Cache-Control: no-store absent");
            Assert.Contains("no-cache", reponse.Headers.Pragma.ToString());
        }

        using var f = Actif();
        var http = f.CreateClient();
        var (ok, _) = await Lire(http, $"/api/me/space/{route}?{Periode}", Jeton(CompteId, 92, MatA));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        VerifierEntetes(ok, "200");

        var (refus, _) = await Lire(http, $"/api/me/space/{route}?{Periode}", Jeton(99999, 92, MatA));        // compte inconnu : sans SELF:READ
        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
        VerifierEntetes(refus, "403");

        var (saisie, _) = await Lire(http, $"/api/me/space/{route}?from=pas-une-date", Jeton(CompteId, 92, MatA));
        Assert.Equal(HttpStatusCode.BadRequest, saisie.StatusCode);
        VerifierEntetes(saisie, "400");

        var (desactivee, _) = await Lire(_f.CreateClient(), $"/api/me/space/{route}?{Periode}", Jeton(CompteId, 92, MatA));
        Assert.Equal(HttpStatusCode.NotFound, desactivee.StatusCode);
        VerifierEntetes(desactivee, "404");
    }

    // ---------------------------------------------------------------- limites connues démontrées

    [Fact]
    public async Task A_token_is_refused_as_soon_as_the_account_row_no_longer_carries_its_matricule()
    {
        // Correction : la garde relit le compte à chaque appel et compare le matricule du jeton au matricule courant.
        using var f = Actif();
        var http = f.CreateClient();
        var jeton = Jeton(CompteId, 92, MatA);
        var url = $"/api/me/space/weeks?{Periode}";
        Assert.Equal(HttpStatusCode.OK, (await Lire(http, url, jeton)).Reponse.StatusCode);

        T_RESPONSABLE compte;
        lock (_f.Store)
            compte = _f.Store.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == CompteId);
        var ancien = compte.MATRICULE;
        try
        {
            compte.MATRICULE = "N300";                                 // la RH réaffecte le compte à un autre salarié
            var (change, texte) = await Lire(http, url, jeton);
            Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
            Assert.Contains("a changé", texte);
            Assert.DoesNotContain("40", texte);                        // aucune donnée du salarié B ni de A
            // Le jeton qui porte le NOUVEAU matricule, lui, est accepté et ne lit rien des données de A (aucune semaine pour N300).
            var (pourN, corpsN) = await Lire(http, url, Jeton(CompteId, 92, "N300"));
            Assert.Equal(HttpStatusCode.OK, pourN.StatusCode);
            using (var j = JsonDocument.Parse(corpsN))
                Assert.Equal(0, j.RootElement.GetProperty("semaines").GetArrayLength());

            compte.MATRICULE = null;                                   // la RH retire le matricule
            Assert.Equal(HttpStatusCode.Forbidden, (await Lire(http, url, jeton)).Reponse.StatusCode);
        }
        finally
        {
            compte.MATRICULE = ancien;
        }
        Assert.Equal(HttpStatusCode.OK, (await Lire(http, url, jeton)).Reponse.StatusCode);   // rétabli : le jeton refonctionne
    }

    [Fact]
    public async Task A_token_is_refused_once_the_account_is_deleted()
    {
        using var f = Actif();
        var http = f.CreateClient();
        var jeton = Jeton(SupprimeId, 92, "S500");
        var url = $"/api/me/space/weeks?{Periode}";
        Assert.Equal(HttpStatusCode.OK, (await Lire(http, url, jeton)).Reponse.StatusCode);
        T_RESPONSABLE compte;
        lock (_f.Store)
        {
            compte = _f.Store.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == SupprimeId);
            _f.Store.GetRepo<T_RESPONSABLE>().Items.Remove(compte);
        }
        try
        {
            var (refus, texte) = await Lire(http, url, jeton);
            Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);
            Assert.Contains("n'existe plus", texte);
        }
        finally
        {
            lock (_f.Store)
                _f.Store.GetRepo<T_RESPONSABLE>().Items.Add(compte);
        }
    }

    [Fact]
    public async Task Two_accounts_sharing_a_matricule_are_both_refused_and_a_unique_matricule_works()
    {
        // Correction : un matricule porté par plus d'un compte refuse TOUS les porteurs (403), sans citer l'autre compte.
        using var f = Actif();
        var http = f.CreateClient();
        var url = $"/api/me/space/weeks?{Periode}";
        T_RESPONSABLE doublon;
        lock (_f.Store)
            doublon = _f.Store.GetRepo<T_RESPONSABLE>().Items.Single(u => u.IDRESPONSABLE == DoublonId);
        var ancien = doublon.MATRICULE;
        Assert.Equal(HttpStatusCode.OK, (await Lire(http, url, Jeton(CompteId, 92, MatA))).Reponse.StatusCode);   // matricule unique
        try
        {
            foreach (var variante in new[] { MatA, " a100 " })                                   // même clé : casse et espaces ignorés
            {
                doublon.MATRICULE = variante;
                foreach (var (id, matricule) in new[] { (CompteId, MatA), (DoublonId, variante.Trim().ToUpperInvariant()) })
                {
                    var (reponse, texte) = await Lire(http, url, Jeton(id, 92, matricule));
                    Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
                    Assert.Contains("plusieurs comptes", texte);
                    Assert.DoesNotContain("adv-", texte);                                       // aucun autre compte désigné
                    Assert.DoesNotContain("1040", texte);
                    Assert.DoesNotContain("semaines", texte);                                   // aucune donnée
                }
            }
        }
        finally
        {
            doublon.MATRICULE = ancien;
        }
        Assert.Equal(HttpStatusCode.OK, (await Lire(http, url, Jeton(CompteId, 92, MatA))).Reponse.StatusCode);   // doublon levé
    }

    // ---------------------------------------------------------------- chargement partagé des congés SAGE

    private static Dictionary<string, List<LeaveDay>> Tenant(params (string Cle, int Jour)[] gens)
        => gens.ToDictionary(g => g.Cle, g => new List<LeaveDay> { new(new DateTime(2026, 11, g.Jour), 1m, "CP") });

    [Fact]
    public async Task Cold_leave_loads_of_different_users_share_one_tenant_read()
    {
        // Correction : 20 salariés différents qui arrivent ensemble à froid ne déclenchent qu'UNE lecture du tenant.
        var cache = new SelfLeaveCache();
        var horloge = TimeProvider.System;
        var lectures = 0;
        Func<Task<Dictionary<string, List<LeaveDay>>>> lecture = async () =>
        {
            Interlocked.Increment(ref lectures);
            await Task.Delay(150);
            return Tenant(("K1", 3), ("K2", 4));
        };

        var parts = await Task.WhenAll(Enumerable.Range(1, 20).Select(i =>
            cache.ObtenirAsync(i, "SAGE", i == 2 ? "K2" : "K" + i, horloge, lecture)));
        Assert.Equal(1, lectures);                                     // une seule lecture du tenant, partagée par les 20 salariés
        Assert.Equal(1, cache.LecturesTenant);
        Assert.Equal(3, parts[0].Single().Date.Day);                   // K1 : sa part
        Assert.Equal(4, parts[1].Single().Date.Day);                   // K2 : sa part
        Assert.All(parts.Skip(2), p => Assert.Empty(p));               // les autres : rien, jamais la part d'un autre

        await Task.WhenAll(Enumerable.Range(1, 20).Select(i => cache.ObtenirAsync(i, "SAGE", "K" + i, horloge, lecture)));
        Assert.Equal(1, lectures);                                     // tous servis par le cache : aucune lecture de plus
    }

    [Fact]
    public async Task The_shared_tenant_read_expires_and_is_reloaded_for_the_next_user()
    {
        var cache = new SelfLeaveCache();
        var horloge = new HorlogeFixe(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero));
        var lectures = 0;
        Func<Task<Dictionary<string, List<LeaveDay>>>> lecture = () =>
        {
            Interlocked.Increment(ref lectures);
            return Task.FromResult(Tenant(("K1", 3), ("K2", 4)));
        };

        await cache.ObtenirAsync(1, "SAGE", "K1", horloge, lecture);
        horloge.Avancer(SelfLeaveCache.Duree - TimeSpan.FromSeconds(1));
        await cache.ObtenirAsync(2, "SAGE", "K2", horloge, lecture);
        Assert.Equal(1, lectures);                                     // encore dans le délai : réutilisé
        horloge.Avancer(TimeSpan.FromSeconds(2));
        var apres = await cache.ObtenirAsync(3, "SAGE", "K2", horloge, lecture);
        Assert.Equal(2, lectures);                                     // délai dépassé : une nouvelle lecture
        Assert.Equal(4, apres.Single().Date.Day);
    }

    [Fact]
    public async Task A_failed_tenant_read_is_shared_by_the_waiting_users_and_never_kept()
    {
        var cache = new SelfLeaveCache();
        var horloge = TimeProvider.System;
        var lectures = 0;
        Func<Task<Dictionary<string, List<LeaveDay>>>> echec = async () =>
        {
            Interlocked.Increment(ref lectures);
            await Task.Delay(100);
            throw new InvalidOperationException("source SAGE indisponible");
        };

        var essais = await Task.WhenAll(Enumerable.Range(1, 5).Select(i =>
            Record.ExceptionAsync(() => cache.ObtenirAsync(i, "SAGE", "K" + i, horloge, echec))));
        Assert.All(essais, e => Assert.IsType<InvalidOperationException>(e));
        Assert.Equal(1, lectures);                                     // les 5 utilisateurs en attente partagent le même échec

        var reussi = await cache.ObtenirAsync(1, "SAGE", "K1", horloge, () => Task.FromResult(Tenant(("K1", 3))));
        Assert.Single(reussi);                                         // l'échec n'a pas été conservé : la lecture suivante réessaie
    }

    [Fact]
    public async Task A_waiting_user_that_cancels_does_not_cancel_the_shared_read()
    {
        var cache = new SelfLeaveCache();
        var horloge = TimeProvider.System;
        var liberer = new TaskCompletionSource<Dictionary<string, List<LeaveDay>>>();
        var premier = cache.ObtenirAsync(1, "SAGE", "K1", horloge, () => liberer.Task);
        using var annulation = new CancellationTokenSource();
        var suivant = cache.ObtenirAsync(2, "SAGE", "K2", horloge, () => liberer.Task, annulation.Token);
        annulation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => suivant);

        liberer.SetResult(Tenant(("K1", 3)));
        Assert.Single(await premier);                                  // la lecture partagée a abouti pour le premier
        Assert.Equal(1, cache.LecturesTenant);
    }

    [Fact]
    public async Task Different_bases_never_share_a_read_or_a_result()
    {
        var cache = new SelfLeaveCache();
        var horloge = TimeProvider.System;
        var a = await cache.ObtenirAsync(1, "SAGE_A", "K1", horloge, () => Task.FromResult(Tenant(("K1", 3))));
        var b = await cache.ObtenirAsync(1, "SAGE_B", "K1", horloge, () => Task.FromResult(new Dictionary<string, List<LeaveDay>>()));
        Assert.Single(a);
        Assert.Empty(b);
        Assert.Equal(2, cache.LecturesTenant);
    }

    [Fact]
    public void The_cache_exposes_no_member_that_returns_the_shared_tenant_result()
    {
        // Le résultat partagé (tous les salariés) reste privé : les seuls membres publics qui rendent des congés sont ObtenirAsync
        // (la part d'UNE clé, copiée) ; aucun ne rend le dictionnaire du tenant.
        const System.Reflection.BindingFlags tous = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
        var membres = typeof(SelfLeaveCache).GetMembers(tous);
        var typesRendus = membres.OfType<System.Reflection.MethodInfo>().Select(m => m.ReturnType.ToString())
            .Concat(membres.OfType<System.Reflection.PropertyInfo>().Select(m => m.PropertyType.ToString()))
            .Concat(membres.OfType<System.Reflection.FieldInfo>().Select(m => m.FieldType.ToString()));
        Assert.DoesNotContain(typesRendus, t => t.Contains("Dictionary"));
    }

    [Fact]
    public void The_memory_ceiling_is_bounded()
    {
        Assert.Equal(512, SelfLeaveCache.MaxEntrees);
        Assert.Equal(8, SelfLeaveCache.MaxBases);
        Assert.Equal(TimeSpan.FromMinutes(5), SelfLeaveCache.Duree);
    }

    [Fact]
    public async Task The_leave_cache_never_serves_an_entry_to_another_user_or_another_matricule()
    {
        var cache = new SelfLeaveCache();
        var horloge = TimeProvider.System;
        var jour = new LeaveDay(new DateTime(2026, 11, 3), 1m, "CP");
        var tenant = new Dictionary<string, List<LeaveDay>> { ["A100"] = new() { jour } };

        var pourA = await cache.ObtenirAsync(1, "SAGE", "A100", horloge, () => Task.FromResult(tenant));
        var pourB = await cache.ObtenirAsync(2, "SAGE", "B200", horloge, () => Task.FromResult(tenant));
        var memeUtilisateurAutreMatricule = await cache.ObtenirAsync(1, "SAGE", "B200", horloge, () => Task.FromResult(tenant));
        var autreBase = await cache.ObtenirAsync(1, "AUTRE", "A100", horloge, () => Task.FromResult(new Dictionary<string, List<LeaveDay>>()));

        Assert.Single(pourA);
        Assert.Empty(pourB);
        Assert.Empty(memeUtilisateurAutreMatricule);
        Assert.Empty(autreBase);

        // La liste rendue est une copie : la modifier ne change ni le résultat partagé ni les lectures suivantes.
        pourA.Clear();
        var encore = await cache.ObtenirAsync(3, "SAGE", "A100", horloge, () => Task.FromResult(tenant));
        Assert.Single(encore);
    }

    private sealed class HorlogeFixe : TimeProvider
    {
        private DateTimeOffset _maintenant;
        public HorlogeFixe(DateTimeOffset depart) => _maintenant = depart;
        public void Avancer(TimeSpan duree) => _maintenant += duree;
        public override DateTimeOffset GetUtcNow() => _maintenant;
    }
}
