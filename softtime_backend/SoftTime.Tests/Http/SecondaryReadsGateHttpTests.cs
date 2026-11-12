using System.Net;
using System.Text;

using SoftTime.Api.Authorization;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Lectures secondaires des écrans des portes 3 à 6 (filtre de période, mode de pointage) :
/// <list type="bullet">
///   <item>GET card-paie : ouverte aux portes 2, 3, 4, 5 et 6 (filtre de période de leurs écrans), refusée sans aucune d'elles ;</item>
///   <item>GET clock-params : ouverte aux portes 2 et 3 (validation hebdomadaire), refusée aux autres ;</item>
///   <item>écritures de CardPaie et de ClockParams, GET card-paie/paged et recherches de DepartementService : inchangées (porte 2).</item>
/// </list>
/// Chaque compte a la porte 1 déduite de SAGE:&lt;base&gt;:READ (usage de la base), comme un vrai compte de travail : la porte 1
/// seule ne suffit pas.
/// </summary>
public class SecondaryReadsGateHttpTests : IClassFixture<ApiFactory>
{
    private const decimal LegacyRoleId = 97;
    private const decimal LegacyId = 10710;

    /// <summary>Profil → (identifiant, permission fine qui donne la porte de l'écran).</summary>
    private static readonly Dictionary<string, (decimal Id, string? Code)> Profiles = new()
    {
        ["porte1"] = (10700, null),
        ["porte2"] = (10702, "PARAMS:CARDPAIE"),
        ["porte3"] = (10703, "PROC:CORRECTED_HOURS"),
        ["porte4"] = (10704, "OTHER:CANTEEN"),
        ["porte5"] = (10705, "TRACE:AUDIT"),
        ["porte6"] = (10706, "REPORTS:REPORTS"),
    };

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public SecondaryReadsGateHttpTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        Seed();
    }

    private void Seed()
    {
        lock (_f.Store)
        {
            var users = _f.Store.Get<Responsable>().Items;
            if (users.Any(u => u.IdResponsable == LegacyId)) return;
            var perms = _f.Store.Get<UserPermission>().Items;
            var access = _f.Store.Get<BddAutorisee>().Items;
            foreach (var (login, (id, code)) in Profiles)
            {
                users.Add(new Responsable { IdResponsable = id, IdRole = 91, Login = $"sec-{login}", NomResponsable = login });
                access.Add(new BddAutorisee { IdBddAutorise = (int)id, IdResponsable = id, IdBdd = 3033 });
                foreach (var c in new[] { $"SAGE:{ApiFactory.SageName}:READ", "POINTEUSE:PTG:READ", code })
                    if (c is not null)
                        perms.Add(new UserPermission { IdResponsable = id, PermissionCode = c, IsGranted = true });
            }
            // Compte hérité : portes 1, 3, 4, 5 et 6 sur le rôle, aucune permission fine enregistrée.
            _f.Store.Get<Role>().Items.Add(new Role { IdRole = LegacyRoleId, NomRole = "sec-herite" });
            foreach (var gate in new[] { 1, 3, 4, 5, 6 })
                _f.Store.Get<DroitRole>().Items.Add(new DroitRole { IdDroitRole = 9700 + gate, IdRole = LegacyRoleId, IdDroit = gate });
            users.Add(new Responsable { IdResponsable = LegacyId, IdRole = LegacyRoleId, Login = "sec-herite", NomResponsable = "herite" });
            access.Add(new BddAutorisee { IdBddAutorise = (int)LegacyId, IdResponsable = LegacyId, IdBdd = 3033 });
        }
    }

    private static string? Jeton(string profil) => profil switch
    {
        "anonyme" => null,
        "admin" => ApiFactory.TokenFor("admin"),
        "herite" => ApiFactory.Token(LegacyId, LegacyRoleId, 1, 3, 4, 5, 6),
        _ => ApiFactory.Token(Profiles[profil].Id, 91)
    };

    private async Task<(HttpStatusCode Code, string Texte)> Appeler(string profil, string methode, string url, string? corps = null)
    {
        var req = new HttpRequestMessage(new HttpMethod(methode), url);
        if (Jeton(profil) is string t)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {t}");
        req.Headers.TryAddWithoutValidation("X-Sage-Database", ApiFactory.SageName);
        req.Headers.TryAddWithoutValidation("X-Pointeuse-Database", "PTG");
        if (corps is not null)
            req.Content = new StringContent(corps, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------------------------- GET card-paie

    [Theory]
    [InlineData("porte2")]
    [InlineData("porte3")]
    [InlineData("porte4")]
    [InlineData("porte5")]
    [InlineData("porte6")]
    [InlineData("admin")]
    [InlineData("herite")]
    public async Task CardPaieList_AccountWithAScreenGateUsingIt_Returns200(string profil)
    {
        // Act
        var (code, texte) = await Appeler(profil, "GET", "/api/v1/card-paie");

        // Assert
        code.Should().Be(HttpStatusCode.OK, texte);
    }

    [Fact]
    public async Task CardPaieList_AccountWithoutGates2To6_Returns403WithGateMessage()
    {
        // Act
        var (code, texte) = await Appeler("porte1", "GET", "/api/v1/card-paie");

        // Assert
        code.Should().Be(HttpStatusCode.Forbidden);
        texte.Should().Contain(GateRefusal.Message);
    }

    [Theory]
    [InlineData("GET", "/api/v1/card-paie")]
    [InlineData("GET", "/api/v1/clock-params")]
    [InlineData("GET", "/api/v1/card-paie/paged?page=1&pageSize=10")]
    [InlineData("POST", "/api/v1/card-paie")]
    [InlineData("PUT", "/api/v1/clock-params")]
    public async Task SecondaryRoutes_Anonymous_Return401(string methode, string url)
    {
        // Act
        var (code, _) = await Appeler("anonyme", methode, url, methode == "GET" ? null : "{}");

        // Assert
        code.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------------------- écritures et routes restées porte 2

    public static IEnumerable<object[]> CardPaieWrites()
    {
        var routes = new (string Methode, string Url, string? Corps)[]
        {
            ("GET", "/api/v1/card-paie/paged?page=1&pageSize=10", null),
            ("POST", "/api/v1/card-paie", "{}"),
            ("PUT", "/api/v1/card-paie/1", "{}"),
            ("DELETE", "/api/v1/card-paie/1", null),
            ("DELETE", "/api/v1/card-paie/by-correspondence?matriculeSage=X", null),
            ("POST", "/api/v1/card-paie/auto-map", null),
            ("PUT", "/api/v1/clock-params", "{}"),
            ("GET", "/api/v1/departement-service/lookup/X", null),
            ("POST", "/api/v1/departement-service/lookup-batch", "[\"X\"]"),
        };
        foreach (var profil in new[] { "porte3", "porte4", "porte5", "porte6", "herite" })
            foreach (var (m, u, c) in routes)
                yield return new object[] { profil, m, u, c ?? string.Empty };
    }

    [Theory]
    [MemberData(nameof(CardPaieWrites))]
    public async Task Parameters2Routes_AccountWithoutParametersGate_Return403AndChangeNothing(
        string profil, string methode, string url, string corps)
    {
        // Arrange
        var avant = _f.Snapshot();

        // Act
        var (code, texte) = await Appeler(profil, methode, url, corps.Length > 0 ? corps : null);

        // Assert
        code.Should().Be(HttpStatusCode.Forbidden, $"{profil} {methode} {url}");
        texte.Should().Contain(GateRefusal.Message);
        _f.Snapshot().Should().Be(avant);
    }

    [Theory]
    [InlineData("POST", "/api/v1/card-paie", "{}")]
    [InlineData("PUT", "/api/v1/card-paie/1", "{}")]
    [InlineData("DELETE", "/api/v1/card-paie/1", null)]
    [InlineData("POST", "/api/v1/card-paie/auto-map", null)]
    public async Task CardPaieWrites_ParametersGateWithoutWritePermission_Return403(string methode, string url, string? corps)
    {
        // Arrange
        var avant = _f.Snapshot();

        // Act
        // porte2 n'a que PARAMS:CARDPAIE (lecture de l'écran), aucune permission CREATE / UPDATE / DELETE.
        var (code, texte) = await Appeler("porte2", methode, url, corps);

        // Assert
        code.Should().Be(HttpStatusCode.Forbidden);
        texte.Should().Contain(FinePermissionAccess.ActionDenied);
        _f.Snapshot().Should().Be(avant);
    }

    // ---------------------------------------------------------------------------------- GET clock-params

    [Theory]
    [InlineData("porte3")]
    [InlineData("admin")]
    [InlineData("herite")]
    public async Task ClockParamsRead_ParametersOrProcessingGate_Returns200(string profil)
    {
        // Act
        var (code, texte) = await Appeler(profil, "GET", "/api/v1/clock-params");

        // Assert
        code.Should().Be(HttpStatusCode.OK, texte);
    }

    [Theory]
    [InlineData("porte1")]
    [InlineData("porte4")]
    [InlineData("porte5")]
    [InlineData("porte6")]
    public async Task ClockParamsRead_WithoutParametersOrProcessingGate_Returns403(string profil)
    {
        // Act
        var (code, texte) = await Appeler(profil, "GET", "/api/v1/clock-params");

        // Assert
        code.Should().Be(HttpStatusCode.Forbidden);
        texte.Should().Contain(GateRefusal.Message);
    }
}
