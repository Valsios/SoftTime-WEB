using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

using SoftTime.Application.Abstractions.SelfService;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Services.SelfService;

namespace SoftTime.Tests.Http;

/// <summary>
/// Revue adverse de « Mon espace » : chaque test tente de casser la garde (jeton falsifié, comptes cumulant les droits,
/// période hors limites, mise en cache, compte modifié ou supprimé, matricule partagé, cache partagé des congés SAGE).
/// </summary>
public class SelfSpaceAdversarialTests : IClassFixture<ApiFactory>
{
    private const string MatA = "A100";
    private const string MatB = "B200";
    private const decimal CompteId = 10401;       // porteur du jeton de A
    private const decimal DoublonId = 10402;      // second compte, même matricule que A
    private const decimal HybrideId = 10403;      // porte 1 + self.read, une base
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

    private void Seed(InMemoryUnitOfWork store)
    {
        var users = store.Get<Responsable>().Items;
        if (users.Any(u => u.IdResponsable == CompteId))
            return;
        store.Get<Role>().Items.Add(new Role { IdRole = 92, NomRole = "Salarié" });
        store.Get<RolePermission>().Items.Add(new RolePermission { Id = 9201, IdRole = 92, PermissionCode = "self.read", IsGranted = true });
        store.Get<Role>().Items.Add(new Role { IdRole = 94, NomRole = "Hybride" });
        foreach (var (id, code) in new[] { (9401, "self.read"), (9402, $"sage.{ApiFactory.SageId}.read"), (9403, "pointeuse.1.read") })
            store.Get<RolePermission>().Items.Add(new RolePermission { Id = id, IdRole = 94, PermissionCode = code, IsGranted = true });

        users.Add(new Responsable { IdResponsable = CompteId, IdRole = 92, Login = "adv-1", NomResponsable = "x", Matricule = MatA });
        users.Add(new Responsable { IdResponsable = DoublonId, IdRole = 92, Login = "adv-doublon", NomResponsable = "x", Matricule = "D400" });
        users.Add(new Responsable { IdResponsable = HybrideId, IdRole = 94, Login = "adv-hybride", NomResponsable = "x", Matricule = MatB });
        users.Add(new Responsable { IdResponsable = SupprimeId, IdRole = 92, Login = "adv-supprime", NomResponsable = "x", Matricule = "S500" });
        users.Add(new Responsable { IdResponsable = HybrideDeuxBasesId, IdRole = 94, Login = "adv-hybride2", NomResponsable = "x", Matricule = "HB300" });
        var acces = store.Get<BddAutorisee>().Items;
        foreach (var (id, idBase, n) in new[] { (CompteId, 3033, 1), (DoublonId, 3033, 2), (HybrideId, 3033, 3), (HybrideDeuxBasesId, 3033, 4), (HybrideDeuxBasesId, 3034, 5), (SupprimeId, 3033, 6) })
            acces.Add(new BddAutorisee { IdBddAutorise = 9400 + n, IdResponsable = id, IdBdd = idBase });

        var cartes = store.Get<CardPaie>().Items;
        cartes.Add(new CardPaie { Id = 7001, SageMatricule = MatA, SageBdd = ApiFactory.SageName, PointeuseBdd = "PTG" });
        cartes.Add(new CardPaie { Id = 7003, SageMatricule = "S500", SageBdd = ApiFactory.SageName, PointeuseBdd = "PTG" });
        cartes.Add(new CardPaie { Id = 7004, SageMatricule = "N300", SageBdd = ApiFactory.SageName, PointeuseBdd = "PTG" });
        cartes.Add(new CardPaie { Id = 7002, SageMatricule = MatB, SageBdd = ApiFactory.SageName, PointeuseBdd = "PTG" });
        var hs = store.Get<HsValidee>().Items;
        hs.Add(new HsValidee { Id = 1, BddSage = ApiFactory.SageName, Matricules = MatA, DateDeb = LundiDe(_aujourdhui.AddDays(-21)), Ht = 40m, Hs = 10m, ValidationHs = true });
        hs.Add(new HsValidee { Id = 2, BddSage = ApiFactory.SageName, Matricules = MatB, DateDeb = LundiDe(_aujourdhui.AddDays(-28)), Ht = 50m, Hs = 25m, ValidationHs = true });
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

    private static async Task<(HttpResponseMessage Reponse, string Texte)> Lire(HttpClient http, string url, string? jeton, string? baseSage = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (jeton is not null)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {jeton}");
        if (baseSage is not null)
            req.Headers.TryAddWithoutValidation("X-Sage-Database", baseSage);
        var r = await http.SendAsync(req);
        return (r, await r.Content.ReadAsStringAsync());
    }

    private static decimal HeuresSup(string texte)
    {
        using var j = JsonDocument.Parse(texte);
        return j.RootElement.GetProperty("data").GetProperty("semaines")[0].GetProperty("heuresSupplementaires").GetDecimal();
    }

    // ---------------------------------------------------------------- jeton falsifié

    [Theory]
    [InlineData("mauvaise-cle")]
    [InlineData("sans-signature")]
    [InlineData("n-importe-quoi")]
    public async Task SelfSpace_ForgedOrUnsignedToken_IsRefusedWhateverMatriculeItClaims(string cas)
    {
        // Arrange
        using var f = Actif();
        var mauvaiseCle = Jeton(CompteId, 92, MatB, "une-autre-cle-de-signature-0123456789-abcdefghijklmnop");
        var jeton = cas switch
        {
            "mauvaise-cle" => mauvaiseCle,
            "sans-signature" => mauvaiseCle[..mauvaiseCle.LastIndexOf('.')] + ".",
            _ => "n-importe-quoi"
        };

        // Act
        var (reponse, _) = await Lire(f.CreateClient(), $"/api/v1/me/space/weeks?{Periode}", jeton);

        // Assert
        reponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- comptes qui cumulent les droits

    [Fact]
    public async Task SelfSpace_Gate1AccountWithSelfReadAndOneBase_ReadsOwnDataOnly()
    {
        // Arrange
        using var f = Actif();
        var jeton = Jeton(HybrideId, 94, MatB, portes: 1);

        // Act
        var (confirmee, texte) = await Lire(f.CreateClient(), $"/api/v1/me/space/weeks?{Periode}", jeton, ApiFactory.SageName);

        // Assert
        confirmee.StatusCode.Should().Be(HttpStatusCode.OK);       // l'en-tête ne fait que confirmer l'unique base autorisée
        HeuresSup(texte).Should().Be(25m);                         // les données de B, jamais celles de A
    }

    [Fact]
    public async Task SelfSpace_Gate1AccountWithForgedOtherBase_IsRefused()
    {
        // Arrange
        using var f = Actif();
        var jeton = Jeton(HybrideId, 94, MatB, portes: 1);

        // Act
        var (autre, _) = await Lire(f.CreateClient(), $"/api/v1/me/space/weeks?{Periode}", jeton, ApiFactory.OtherSageName);

        // Assert
        autre.StatusCode.Should().Be(HttpStatusCode.Forbidden);    // base non autorisée forgée : refusée avant la garde
    }

    [Fact]
    public async Task SelfSpace_Gate1AccountWithTwoAuthorizedBases_IsRefusedWithExplicitMessage()
    {
        // Arrange
        using var f = Actif();
        var jeton = Jeton(HybrideDeuxBasesId, 94, "HB300", portes: 1);

        // Act
        var (reponse, texte) = await Lire(f.CreateClient(), $"/api/v1/me/space/weeks?{Periode}", jeton, ApiFactory.SageName);

        // Assert
        reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        texte.Should().Contain("exactement une base");
    }

    // ---------------------------------------------------------------- période hors limites

    private static readonly DateTime AujourdhuiFixe = new(2026, 11, 30);
    private static readonly DateTime DebutFenetre = new(2026, 8, 1);

    [Fact]
    public void Borner_ExactlyFourMonths_IsAccepted()
    {
        // Act
        var periode = SelfSpacePeriodRules.Borner(DebutFenetre, AujourdhuiFixe, AujourdhuiFixe);

        // Assert
        periode.Should().Be((DebutFenetre, AujourdhuiFixe));
    }

    [Fact]
    public void Borner_DistantEnd_IsCappedToToday()
    {
        // Act
        var periode = SelfSpacePeriodRules.Borner(AujourdhuiFixe, AujourdhuiFixe.AddYears(10), AujourdhuiFixe);

        // Assert
        periode.Should().Be((AujourdhuiFixe, AujourdhuiFixe));
    }

    [Fact]
    public void Borner_NoDates_DefaultsToCurrentMonth()
    {
        // Act
        var periode = SelfSpacePeriodRules.Borner(null, null, AujourdhuiFixe);

        // Assert
        periode.Should().Be((new DateTime(2026, 11, 1), AujourdhuiFixe));
    }

    public static IEnumerable<object[]> PeriodesRefusees()
    {
        yield return new object[] { DebutFenetre.AddDays(-1), AujourdhuiFixe };                 // un jour avant la fenêtre
        yield return new object[] { AujourdhuiFixe.AddDays(1), AujourdhuiFixe.AddDays(2) };     // début futur
        yield return new object[] { AujourdhuiFixe, AujourdhuiFixe.AddDays(-1) };               // fin avant début
        yield return new object[] { DateTime.MinValue, AujourdhuiFixe };                        // année 1
    }

    [Theory]
    [MemberData(nameof(PeriodesRefusees))]
    public void Borner_OutOfBoundsPeriod_ThrowsDomainException(DateTime debut, DateTime fin)
    {
        // Act
        var act = () => SelfSpacePeriodRules.Borner(debut, fin, AujourdhuiFixe);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("from=pas-une-date")]
    [InlineData("from=2026-13-45")]
    [InlineData("to=%27%3BDROP%20TABLE%20T_HS_VALIDER--")]
    public async Task SelfSpace_MalformedDate_Is400NeverDataNor500(string requete)
    {
        // Arrange
        using var f = Actif();

        // Act
        var (reponse, texte) = await Lire(f.CreateClient(), $"/api/v1/me/space/weeks?{requete}", Jeton(CompteId, 92, MatA));

        // Assert
        reponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        texte.Should().NotContain("semaines");
    }

    // ---------------------------------------------------------------- en-têtes de réponse

    private static void DoitInterdireLeCache(HttpResponseMessage reponse, string cas)
    {
        (reponse.Headers.CacheControl?.NoStore ?? false).Should().BeTrue($"{cas} : Cache-Control: no-store absent");
        reponse.Headers.Pragma.ToString().Should().Contain("no-cache");
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("weeks")]
    [InlineData("anomalies")]
    [InlineData("absences")]
    [InlineData("delays")]
    public async Task SelfSpace_AnyResponseIncludingRefusals_ForbidsCaching(string route)
    {
        // Arrange : [NoStore] sur SelfSpaceController, pour un succès, un refus 403, une erreur de saisie 400 et le 404 de
        // la fonction désactivée.
        using var f = Actif();
        var http = f.CreateClient();

        // Act
        var (ok, _) = await Lire(http, $"/api/v1/me/space/{route}?{Periode}", Jeton(CompteId, 92, MatA));
        var (refus, _) = await Lire(http, $"/api/v1/me/space/{route}?{Periode}", Jeton(99999, 92, MatA));   // sans self.read
        var (saisie, _) = await Lire(http, $"/api/v1/me/space/{route}?from=pas-une-date", Jeton(CompteId, 92, MatA));
        var (desactivee, _) = await Lire(_f.CreateClient(), $"/api/v1/me/space/{route}?{Periode}", Jeton(CompteId, 92, MatA));

        // Assert
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        DoitInterdireLeCache(ok, "200");
        refus.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        DoitInterdireLeCache(refus, "403");
        saisie.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        DoitInterdireLeCache(saisie, "400");
        desactivee.StatusCode.Should().Be(HttpStatusCode.NotFound);
        DoitInterdireLeCache(desactivee, "404");
    }

    // ---------------------------------------------------------------- compte modifié ou supprimé pendant la validité du jeton

    [Fact]
    public async Task SelfSpace_AccountMatriculeChangedOrRemoved_TokenIsRefused()
    {
        // Arrange : la garde relit le compte à chaque appel et compare le matricule du jeton au matricule courant.
        using var f = Actif();
        var http = f.CreateClient();
        var jeton = Jeton(CompteId, 92, MatA);
        var url = $"/api/v1/me/space/weeks?{Periode}";
        var (avant, _) = await Lire(http, url, jeton);
        Responsable compte;
        lock (_f.Store)
            compte = _f.Store.Get<Responsable>().Items.Single(u => u.IdResponsable == CompteId);
        var ancien = compte.Matricule;
        try
        {
            // Act
            compte.Matricule = "N300";                                 // la RH réaffecte le compte à un autre salarié
            var (change, texte) = await Lire(http, url, jeton);
            var (pourN, corpsN) = await Lire(http, url, Jeton(CompteId, 92, "N300"));
            compte.Matricule = null;                                   // la RH retire le matricule
            var (retire, _) = await Lire(http, url, jeton);

            // Assert
            avant.StatusCode.Should().Be(HttpStatusCode.OK);
            change.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            texte.Should().Contain("a changé");
            texte.Should().NotContain("semaines");                     // aucune donnée du salarié B ni de A
            pourN.StatusCode.Should().Be(HttpStatusCode.OK);            // le jeton du NOUVEAU matricule ne lit rien de A
            using (var j = JsonDocument.Parse(corpsN))
                j.RootElement.GetProperty("data").GetProperty("semaines").GetArrayLength().Should().Be(0);
            retire.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            compte.Matricule = ancien;
        }
        (await Lire(http, url, jeton)).Reponse.StatusCode.Should().Be(HttpStatusCode.OK);   // rétabli : le jeton refonctionne
    }

    [Fact]
    public async Task SelfSpace_DeletedAccount_TokenIsRefused()
    {
        // Arrange
        using var f = Actif();
        var http = f.CreateClient();
        var jeton = Jeton(SupprimeId, 92, "S500");
        var url = $"/api/v1/me/space/weeks?{Periode}";
        var (avant, _) = await Lire(http, url, jeton);
        Responsable compte;
        lock (_f.Store)
        {
            compte = _f.Store.Get<Responsable>().Items.Single(u => u.IdResponsable == SupprimeId);
            _f.Store.Get<Responsable>().Items.Remove(compte);
        }
        try
        {
            // Act
            var (refus, texte) = await Lire(http, url, jeton);

            // Assert
            avant.StatusCode.Should().Be(HttpStatusCode.OK);
            refus.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            texte.Should().Contain("n'existe plus");
        }
        finally
        {
            lock (_f.Store)
                _f.Store.Get<Responsable>().Items.Add(compte);
        }
    }

    [Theory]
    [InlineData(MatA)]
    [InlineData(" a100 ")]                                             // même clé : casse et espaces ignorés
    public async Task SelfSpace_MatriculeSharedByTwoAccounts_RefusesBothHolders(string variante)
    {
        // Arrange : un matricule porté par plus d'un compte refuse TOUS les porteurs (403), sans citer l'autre compte.
        using var f = Actif();
        var http = f.CreateClient();
        var url = $"/api/v1/me/space/weeks?{Periode}";
        Responsable doublon;
        lock (_f.Store)
            doublon = _f.Store.Get<Responsable>().Items.Single(u => u.IdResponsable == DoublonId);
        var ancien = doublon.Matricule;
        var (unique, _) = await Lire(http, url, Jeton(CompteId, 92, MatA));
        try
        {
            doublon.Matricule = variante;
            foreach (var (id, matricule) in new[] { (CompteId, MatA), (DoublonId, variante.Trim().ToUpperInvariant()) })
            {
                // Act
                var (reponse, texte) = await Lire(http, url, Jeton(id, 92, matricule));

                // Assert
                reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                texte.Should().Contain("plusieurs comptes");
                texte.Should().NotContain("adv-");                     // aucun autre compte désigné
                texte.Should().NotContain("1040");
                texte.Should().NotContain("semaines");                 // aucune donnée
            }
        }
        finally
        {
            doublon.Matricule = ancien;
        }
        unique.StatusCode.Should().Be(HttpStatusCode.OK);              // matricule unique
        (await Lire(http, url, Jeton(CompteId, 92, MatA))).Reponse.StatusCode.Should().Be(HttpStatusCode.OK);   // doublon levé
    }

    // ---------------------------------------------------------------- chargement partagé des congés SAGE

    private static Dictionary<string, List<LeaveDay>> Tenant(params (string Cle, int Jour)[] gens)
        => gens.ToDictionary(g => g.Cle, g => new List<LeaveDay> { new(new DateTime(2026, 11, g.Jour), 1m, "CP") });

    /// <summary>Salarié résolu fictif : seuls l'utilisateur, la base et la clé de matricule servent au cache.</summary>
    private static SelfContext Moi(decimal userId, string sageDb, string cle)
        => new(userId, sageDb, cle, cle, Array.Empty<CardPaie>());

    [Fact]
    public async Task ObtenirAsync_ColdLoadsOfDifferentUsers_ShareOneTenantRead()
    {
        // Arrange : 20 salariés différents qui arrivent ensemble à froid ne déclenchent qu'UNE lecture du tenant.
        var sut = new SelfLeaveCache(TimeProvider.System);
        var lectures = 0;
        Func<Task<Dictionary<string, List<LeaveDay>>>> lecture = async () =>
        {
            Interlocked.Increment(ref lectures);
            await Task.Delay(150);
            return Tenant(("K1", 3), ("K2", 4));
        };

        // Act
        var parts = await Task.WhenAll(Enumerable.Range(1, 20).Select(i => sut.ObtenirAsync(Moi(i, "SAGE", "K" + i), lecture)));
        await Task.WhenAll(Enumerable.Range(1, 20).Select(i => sut.ObtenirAsync(Moi(i, "SAGE", "K" + i), lecture)));

        // Assert
        lectures.Should().Be(1);                                     // une seule lecture, puis tous servis par le cache
        sut.LecturesTenant.Should().Be(1);
        parts[0].Single().Date.Day.Should().Be(3);                   // K1 : sa part
        parts[1].Single().Date.Day.Should().Be(4);                   // K2 : sa part
        parts.Skip(2).Should().OnlyContain(p => p.Count == 0);       // les autres : rien, jamais la part d'un autre
    }

    [Fact]
    public async Task ObtenirAsync_SharedTenantReadExpired_IsReloadedForNextUser()
    {
        // Arrange
        var horloge = new HorlogeFixe(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero));
        var sut = new SelfLeaveCache(horloge);
        var lectures = 0;
        Func<Task<Dictionary<string, List<LeaveDay>>>> lecture = () =>
        {
            Interlocked.Increment(ref lectures);
            return Task.FromResult(Tenant(("K1", 3), ("K2", 4)));
        };
        await sut.ObtenirAsync(Moi(1, "SAGE", "K1"), lecture);

        // Act
        horloge.Avancer(SelfLeaveCache.Duree - TimeSpan.FromSeconds(1));
        await sut.ObtenirAsync(Moi(2, "SAGE", "K2"), lecture);
        var lecturesDansLeDelai = lectures;
        horloge.Avancer(TimeSpan.FromSeconds(2));
        var apres = await sut.ObtenirAsync(Moi(3, "SAGE", "K2"), lecture);

        // Assert
        lecturesDansLeDelai.Should().Be(1);                          // encore dans le délai : réutilisé
        lectures.Should().Be(2);                                     // délai dépassé : une nouvelle lecture
        apres.Single().Date.Day.Should().Be(4);
    }

    [Fact]
    public async Task ObtenirAsync_FailedTenantRead_IsSharedByWaitingUsersAndNeverKept()
    {
        // Arrange
        var sut = new SelfLeaveCache(TimeProvider.System);
        var lectures = 0;
        Func<Task<Dictionary<string, List<LeaveDay>>>> echec = async () =>
        {
            Interlocked.Increment(ref lectures);
            await Task.Delay(100);
            throw new InvalidOperationException("source SAGE indisponible");
        };

        // Act
        var essais = await Task.WhenAll(Enumerable.Range(1, 5).Select(i =>
            Record.ExceptionAsync(() => sut.ObtenirAsync(Moi(i, "SAGE", "K" + i), echec))));
        var reussi = await sut.ObtenirAsync(Moi(1, "SAGE", "K1"), () => Task.FromResult(Tenant(("K1", 3))));

        // Assert
        essais.Should().AllBeOfType<InvalidOperationException>();
        lectures.Should().Be(1);                                     // les 5 utilisateurs en attente partagent le même échec
        reussi.Should().ContainSingle();                             // l'échec n'a pas été conservé : la lecture suivante réessaie
    }

    [Fact]
    public async Task ObtenirAsync_WaitingUserCancels_SharedReadStillCompletes()
    {
        // Arrange
        var sut = new SelfLeaveCache(TimeProvider.System);
        var liberer = new TaskCompletionSource<Dictionary<string, List<LeaveDay>>>();
        var premier = sut.ObtenirAsync(Moi(1, "SAGE", "K1"), () => liberer.Task);
        using var annulation = new CancellationTokenSource();
        var suivant = sut.ObtenirAsync(Moi(2, "SAGE", "K2"), () => liberer.Task, annulation.Token);

        // Act
        annulation.Cancel();
        var attente = () => suivant;
        await attente.Should().ThrowAsync<OperationCanceledException>();
        liberer.SetResult(Tenant(("K1", 3)));

        // Assert
        (await premier).Should().ContainSingle();                    // la lecture partagée a abouti pour le premier
        sut.LecturesTenant.Should().Be(1);
    }

    [Fact]
    public async Task ObtenirAsync_DifferentBases_NeverShareReadOrResult()
    {
        // Arrange
        var sut = new SelfLeaveCache(TimeProvider.System);

        // Act
        var a = await sut.ObtenirAsync(Moi(1, "SAGE_A", "K1"), () => Task.FromResult(Tenant(("K1", 3))));
        var b = await sut.ObtenirAsync(Moi(1, "SAGE_B", "K1"), () => Task.FromResult(new Dictionary<string, List<LeaveDay>>()));

        // Assert
        a.Should().ContainSingle();
        b.Should().BeEmpty();
        sut.LecturesTenant.Should().Be(2);
    }

    [Fact]
    public void SelfLeaveCache_PublicMembers_NeverReturnSharedTenantResult()
    {
        // Arrange : le résultat partagé (tous les salariés) reste privé ; seul ObtenirAsync rend des congés (la part d'UNE
        // clé, copiée) et aucun membre ne rend le dictionnaire du tenant.
        const BindingFlags tous = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var membres = typeof(SelfLeaveCache).GetMembers(tous);

        // Act
        var typesRendus = membres.OfType<MethodInfo>().Select(m => m.ReturnType.ToString())
            .Concat(membres.OfType<PropertyInfo>().Select(m => m.PropertyType.ToString()))
            .Concat(membres.OfType<FieldInfo>().Select(m => m.FieldType.ToString()));

        // Assert
        typesRendus.Should().NotContain(t => t.Contains("Dictionary"));
    }

    [Fact]
    public void SelfLeaveCache_MemoryCeiling_IsBounded()
    {
        // Assert
        SelfLeaveCache.MaxEntrees.Should().Be(512);
        SelfLeaveCache.MaxBases.Should().Be(8);
        SelfLeaveCache.Duree.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ObtenirAsync_OtherUserOrMatricule_NeverGetsAnotherEntry()
    {
        // Arrange
        var sut = new SelfLeaveCache(TimeProvider.System);
        var jour = new LeaveDay(new DateTime(2026, 11, 3), 1m, "CP");
        var tenant = new Dictionary<string, List<LeaveDay>> { ["A100"] = new() { jour } };

        // Act
        var pourA = await sut.ObtenirAsync(Moi(1, "SAGE", "A100"), () => Task.FromResult(tenant));
        var pourB = await sut.ObtenirAsync(Moi(2, "SAGE", "B200"), () => Task.FromResult(tenant));
        var memeUtilisateurAutreMatricule = await sut.ObtenirAsync(Moi(1, "SAGE", "B200"), () => Task.FromResult(tenant));
        var autreBase = await sut.ObtenirAsync(
            Moi(1, "AUTRE", "A100"), () => Task.FromResult(new Dictionary<string, List<LeaveDay>>()));
        pourA.Clear();                                               // la liste rendue est une copie
        var encore = await sut.ObtenirAsync(Moi(3, "SAGE", "A100"), () => Task.FromResult(tenant));

        // Assert
        pourB.Should().BeEmpty();
        memeUtilisateurAutreMatricule.Should().BeEmpty();
        autreBase.Should().BeEmpty();
        encore.Should().ContainSingle();                             // ni le résultat partagé ni les lectures suivantes ne changent
    }

    private sealed class HorlogeFixe : TimeProvider
    {
        private DateTimeOffset _maintenant;
        public HorlogeFixe(DateTimeOffset depart) => _maintenant = depart;
        public void Avancer(TimeSpan duree) => _maintenant += duree;
        public override DateTimeOffset GetUtcNow() => _maintenant;
    }
}
