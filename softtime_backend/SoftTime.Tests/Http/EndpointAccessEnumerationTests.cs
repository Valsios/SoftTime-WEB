using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using SoftTime.Api.Authorization;
using SoftTime.Domain.Entities.SoftTime;

using Xunit.Abstractions;

namespace SoftTime.Tests.Http;

/// <summary>
/// Énumération de TOUTES les actions de TOUS les contrôleurs (métadonnées d'endpoints ASP.NET) :
/// <list type="number">
///   <item>chaque action a un contrôle d'accès EXPLICITE visible dans ses métadonnées (porte : RequireDroit / RequireAnyDroit, ou
///     [AllowAnonymous] listé et justifié), ou figure dans <see cref="ControleDansLeCode"/> (contrôle fait dans le contrôleur ou le
///     service, justifié) : une nouvelle action sans l'un ni l'autre fait échouer le test ;</item>
///   <item>pour un compte Salarié (self.read, matricule, une base, aucune porte, fonction activée), TOUTES les routes hors api/v1/me/space
///     répondent 403, ou 200 avec une liste / page vide ; toute autre réponse est listée comme « échappée » et fait échouer le test.</item>
/// </list>
/// Les contrôles « dans le code » ne se voient pas dans les métadonnées : c'est le balayage du compte Salarié qui les prouve.
/// </summary>
public class EndpointAccessEnumerationTests : IClassFixture<ApiFactory>
{
    private const decimal SalarieId = 10301;
    private const string Mat = "E300";

    private readonly ApiFactory _f;
    private readonly ITestOutputHelper _o;

    public EndpointAccessEnumerationTests(ApiFactory factory, ITestOutputHelper sortie)
    {
        _f = factory;
        _o = sortie;
        lock (factory.Store)
        {
            var users = factory.Store.Get<Responsable>().Items;
            if (users.Any(u => u.IdResponsable == SalarieId))
                return;
            factory.Store.Get<Role>().Items.Add(new Role { IdRole = 93, NomRole = "SalarieEnum" });
            factory.Store.Get<RolePermission>().Items.Add(new RolePermission { Id = 9301, IdRole = 93, PermissionCode = "self.read", IsGranted = true });
            users.Add(new Responsable { IdResponsable = SalarieId, IdRole = 93, Login = "enum-salarie", NomResponsable = "salarie", Matricule = Mat });
            factory.Store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = 93001, IdResponsable = SalarieId, IdBdd = 3033 });
            factory.Store.Get<CardPaie>().Items.Add(new CardPaie { Id = 6001, SageMatricule = Mat, SageBdd = ApiFactory.SageName, PointeuseBdd = "PTG" });
        }
    }

    // ---------------------------------------------------------------------------------- liste blanche justifiée

    /// <summary>Routes anonymes voulues : la connexion et les points d'entrée nécessaires avant toute authentification.</summary>
    private static readonly Dictionary<string, string> Anonymes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["POST api/v1/auth/login"] = "connexion",
        ["GET api/v1/auth/is-first-run"] = "l'écran de connexion décide s'il propose la création du premier administrateur",
        ["POST api/v1/auth/create-admin"] = "création du premier administrateur, refusée dès qu'un compte existe (AuthService.IsFirstRunAsync)",
        ["GET api/v1/auth/password-policy"] = "politique de mot de passe affichée avant connexion",
        ["POST api/v1/auth/refresh"] = "renouvellement par le cookie de rafraîchissement (le jeton d'accès a expiré)",
        ["POST api/v1/auth/logout"] = "déconnexion par le cookie de rafraîchissement, même avec un jeton expiré",
    };

    /// <summary>
    /// Actions SANS porte dans leurs métadonnées : le contrôle est fait dans le contrôleur ou le service. Une ligne PAR Action (méthode + route),
    /// avec le contrôle en cause : une nouvelle action sans porte, même dans un de ces contrôleurs, fait échouer le test. Le balayage du compte
    /// Salarié (test 2) prouve que chacune refuse bien un compte sans porte.
    /// </summary>
    private static readonly Dictionary<string, string> ControleDansLeCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GET api/v1/auth/me"] = "profil du compte connecté (jeton) ; voir OuvertesJustifiees",
        ["GET api/v1/meta/endpoints"] = "catalogue d'API pour le frontend ; voir OuvertesJustifiees",
        ["GET api/v1/card-paie"] = "CatalogService.ListCardsAsync -> EnsureAuthorizedAsync (porte 1)",
        ["GET api/v1/card-paie/paged"] = "CatalogService.ListCardsPagedAsync -> EnsureAuthorizedAsync (porte 1)",
        ["GET api/v1/dashboard/stats"] = "DashboardService.GetStatsAsync -> EnsureAuthorizedAsync (porte 1)",
        ["GET api/v1/dashboard/filters"] = "DashboardService.GetFiltersAsync -> EnsureAuthorizedAsync (porte 1)",
        ["GET api/v1/field-roles"] = "référentiel de champs sans donnée de salarié (balayage : refusé ou vide)",
        ["GET api/v1/import-templates"] = "aide statique ; voir OuvertesJustifiees",
        ["GET api/v1/import-templates/{key}"] = "aide statique ; voir OuvertesJustifiees",
        ["POST api/v1/excel/export"] = "ne lit rien ; voir OuvertesJustifiees",
        ["POST api/v1/excel/export-workbook"] = "ne lit rien ; voir OuvertesJustifiees",
        ["GET api/v1/closing/analysis"] = "ClosingController : FineReadAccess.EnsureAsync (porte 6 ET closing.read)",
        ["GET api/v1/closing/checklist"] = "ClosingController : FineReadAccess.EnsureAsync (porte 6 ET closing.read)",
        ["GET api/v1/journal"] = "JournalService.EnsureCanReadAsync (porte 5)",
        ["GET api/v1/journal/facets"] = "JournalService.EnsureCanReadAsync (porte 5)",
        ["GET api/v1/journal/{id:long}"] = "JournalService.EnsureCanReadAsync (porte 5)",
        ["GET api/v1/pointeuse-databases"] = "200 vide sans porte 1 ni 2 (PeutLireLesBases)",
        ["GET api/v1/pointeuse-databases/paged"] = "page vide sans porte 1 ni 2 (PeutLireLesBases)",
        ["GET api/v1/sage-databases"] = "200 vide sans porte 1 ni 2 (PeutLireLesBases)",
        ["GET api/v1/sage-databases/paged"] = "page vide sans porte 1 ni 2 (PeutLireLesBases)",
        ["GET api/v1/sage-databases/departement-service-options"] = "CatalogService.GetDepartementServiceOptionsAsync -> EnsureAuthorizedAsync (porte 1)",
        ["POST api/v1/sage-databases/activate"] = "CatalogService.ActivateSagePairAsync -> EnsureAuthorizedAsync (porte 1)",
        ["GET api/v1/predictions/landing"] = "PredictionsController : FineReadAccess (porte 6 ET permission fine)",
        ["GET api/v1/predictions/landing/horizon"] = "PredictionsController : FineReadAccess (porte 6 ET permission fine)",
        ["GET api/v1/me/space/summary"] = "SelfServiceGuard : self.read + matricule du jeton",
        ["GET api/v1/me/space/weeks"] = "SelfServiceGuard : self.read + matricule du jeton",
        ["GET api/v1/me/space/anomalies"] = "SelfServiceGuard : self.read + matricule du jeton",
        ["GET api/v1/me/space/absences"] = "SelfServiceGuard : self.read + matricule du jeton",
        ["GET api/v1/me/space/delays"] = "SelfServiceGuard : self.read + matricule du jeton",
    };

    /// <summary>
    /// Routes qui répondent 200 à tout compte authentifié, sans donnée de salarié : justification obligatoire, vérifiée par le balayage.
    /// Toute autre route doit refuser un compte sans porte.
    /// </summary>
    private static readonly Dictionary<string, string> OuvertesJustifiees = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GET api/v1/auth/me"] = "profil du compte connecté seulement (identité tirée du jeton)",
        ["GET api/v1/meta/endpoints"] = "catalogue des routes de l'API pour le frontend, aucune donnée",
        ["GET api/v1/import-templates"] = "aide statique sur les formats d'import (ImportTemplateCatalog), aucune donnée",
        ["GET api/v1/import-templates/{key}"] = "aide statique sur les formats d'import, aucune donnée",
        ["POST api/v1/excel/export"] = "met en forme les données envoyées par le client, ne lit rien",
        ["POST api/v1/excel/export-workbook"] = "met en forme les données envoyées par le client, ne lit rien",
    };

    // ---------------------------------------------------------------------------------- énumération

    private sealed record Action(string Controleur, string Methode, string MlModele, bool Anonyme, bool Authentifie, bool PorteDansMetadonnees);

    private IReadOnlyList<Action> Enumerer()
    {
        var source = _f.Services.GetRequiredService<EndpointDataSource>();
        var liste = new List<Action>();
        foreach (var e in source.Endpoints.OfType<RouteEndpoint>())
        {
            var methodes = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };
            var controleur = e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Name ?? e.DisplayName ?? "?";
            var anonyme = e.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var authentifie = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            var porte = e.Metadata.GetMetadata<RequireDroitAttribute>() is not null || e.Metadata.GetMetadata<RequireAnyDroitAttribute>() is not null;
            foreach (var m in methodes)
                liste.Add(new Action(controleur, m, e.RoutePattern.RawText?.TrimStart('/') ?? string.Empty, anonyme, authentifie, porte));
        }
        return liste;
    }

    [Fact]
    public void Endpoints_EveryAction_HasExplicitAccessControlOrJustification()
    {
        // Arrange
        var actions = Enumerer().Where(a => a.Methode != "HEAD" && a.Methode != "OPTIONS").ToList();

        // Act
        var sansControle = new List<string>();
        foreach (var a in actions)
        {
            var cle = $"{a.Methode} {a.MlModele}";
            if (cle == "GET health")
                continue;                                           // sonde de santé (MapGet hors contrôleur) : ouverte volontairement, aucune donnée
            if (a.Anonyme)
            {
                if (!Anonymes.ContainsKey(cle))
                    sansControle.Add($"{cle} : anonyme et NON listée dans la liste blanche ({a.Controleur})");
                continue;
            }
            if (!a.Authentifie)
                sansControle.Add($"{cle} : ni [Authorize] ni [AllowAnonymous] ({a.Controleur})");
            else if (!a.PorteDansMetadonnees && !ControleDansLeCode.ContainsKey(cle))
                sansControle.Add($"{cle} : authentifiée sans porte ni justification ({a.Controleur})");
        }

        // Assert
        _o.WriteLine($"{actions.Count} actions énumérées ; anonymes : {actions.Count(a => a.Anonyme)} ; avec porte : {actions.Count(a => a.PorteDansMetadonnees)} ; contrôle dans le code : {actions.Count(a => !a.Anonyme && !a.PorteDansMetadonnees)}");
        sansControle.Count.Should().Be(0, "Actions sans contrôle d'accès explicite :\n" + string.Join("\n", sansControle));
    }

    [Fact]
    public void ControleDansLeCode_EveryEntry_IsStillAnUngatedRoute()
    {
        // Act
        var sansPorte = Enumerer().Where(a => !a.Anonyme && !a.PorteDansMetadonnees).Select(a => $"{a.Methode} {a.MlModele}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var perimees = ControleDansLeCode.Keys.Where(k => !sansPorte.Contains(k)).ToList();

        // Assert
        perimees.Count.Should().Be(0, "Entrées périmées (route disparue ou désormais protégée par une porte) : " + string.Join(", ", perimees));
    }

    [Fact]
    public void Anonymes_EveryEntry_IsStillAnAnonymousRoute()
    {
        // Act
        var presentes = Enumerer().Where(a => a.Anonyme).Select(a => $"{a.Methode} {a.MlModele}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var perimees = Anonymes.Keys.Where(k => !presentes.Contains(k)).ToList();

        // Assert
        perimees.Should().BeEmpty("entrées anonymes périmées : " + string.Join(", ", perimees));
    }

    // ---------------------------------------------------------------------------------- balayage du compte Salarié

    private static string JetonSalarie()
    {
        var uid = ((long)SalarieId).ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, uid), new(ClaimTypes.NameIdentifier, uid), new("uid", uid),
            new(ClaimTypes.Name, "salarie"), new("roleId", "93"), new("matricule", Mat), new("rights", string.Empty)
        };
        var jeton = new JwtSecurityToken(ApiFactory.TestIssuer, ApiFactory.TestAudience, claims, expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.TestKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jeton);
    }

    private static string Remplir(string modele)
    {
        var chemin = Regex.Replace(modele, @"\{(\*\*)?([^}:]+)(:[^}]*)?\}", m => m.Groups[3].Value.StartsWith(":int") || m.Groups[3].Value.StartsWith(":long") || m.Groups[3].Value.StartsWith(":decimal") ? "1" : "x");
        return "/" + chemin;
    }

    private static bool EstVide(string texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return false;
        try
        {
            using var json = JsonDocument.Parse(texte);
            if (!json.RootElement.TryGetProperty("data", out var r))
                return false;
            if (r.ValueKind == JsonValueKind.Array)
                return r.GetArrayLength() == 0;
            return r.ValueKind == JsonValueKind.Object && r.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() == 0
                   && r.TryGetProperty("total", out var total) && total.GetInt32() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<(HttpStatusCode Code, string Texte)> Envoyer(HttpClient http, Action a, string jeton, bool entetes)
    {
        async Task<(HttpStatusCode, string)> Une(bool multipart)
        {
            var req = new HttpRequestMessage(new HttpMethod(a.Methode), Remplir(a.MlModele));
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {jeton}");
            if (entetes)
            {
                // Un salarié malveillant peut forger ces en-têtes : ils ne doivent jamais ouvrir quoi que ce soit.
                req.Headers.TryAddWithoutValidation("X-Sage-Database", ApiFactory.SageName);
                req.Headers.TryAddWithoutValidation("X-Pointeuse-Database", "PTG");
            }
            if (a.Methode is "POST" or "PUT" or "PATCH")
            {
                if (multipart)
                {
                    var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[] { 1 }), "file", "x.csv" } };
                    req.Content = form;
                }
                else
                    req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }
            var r = await http.SendAsync(req);
            return (r.StatusCode, await r.Content.ReadAsStringAsync());
        }
        var premier = await Une(false);
        // [Consumes(multipart)] répond 415 au routage, AVANT les filtres d'autorisation : on rejoue en multipart pour atteindre le contrôle d'accès.
        return premier.Item1 == HttpStatusCode.UnsupportedMediaType ? await Une(true) : premier;
    }

    private async Task<List<string>> Balayer(bool entetes)
    {
        using var actif = _f.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("SelfService:Enabled", "true") })));
        var http = actif.CreateClient();
        var jeton = JetonSalarie();
        var echappees = new List<string>();
        var testees = 0;
        foreach (var a in Enumerer().Where(x => x.Methode is not ("HEAD" or "OPTIONS") && !x.MlModele.StartsWith("api/v1/me/space", StringComparison.OrdinalIgnoreCase)))
        {
            var cle = $"{a.Methode} {a.MlModele}";
            if (a.Anonyme || cle == "GET health")
                continue;                                           // routes anonymes : liste blanche, hors du balayage
            var (code, texte) = await Envoyer(http, a, jeton, entetes);
            testees++;

            var refuse = code == HttpStatusCode.Forbidden || (code == HttpStatusCode.OK && EstVide(texte));
            // Sans en-tête de base, un service de données répond 400 « X-Sage-Database » : fermé, aucune donnée.
            var baseManquante = !entetes && code == HttpStatusCode.BadRequest && texte.Contains("X-Sage-Database");
            var ouverteJustifiee = OuvertesJustifiees.ContainsKey(cle) && (code is HttpStatusCode.OK or HttpStatusCode.NotFound);
            if (!(refuse || baseManquante || ouverteJustifiee))
                echappees.Add($"{cle} -> {(int)code} ({a.Controleur})");
        }
        _o.WriteLine($"[{(entetes ? "en-têtes de base forgés" : "sans en-tête")}] {testees} routes testées avec un compte Salarié ; échappées : {echappees.Count}");
        foreach (var e in echappees)
            _o.WriteLine("ÉCHAPPÉE : " + e);
        return echappees;
    }

    [Fact]
    public async Task Endpoints_SalarieAccountOutsideSelfSpace_AreRefusedOrEmpty()
    {
        // Arrange
        var avant = _f.Snapshot();


        // Act
        var avecEntetes = await Balayer(entetes: true);
        var sansEntete = await Balayer(entetes: false);

        // Assert
        avecEntetes.Count.Should().Be(0, "Routes qui échappent au refus (en-têtes de base forgés) :\n" + string.Join("\n", avecEntetes));
        sansEntete.Count.Should().Be(0, "Routes qui échappent au refus (sans en-tête) :\n" + string.Join("\n", sansEntete));
        _f.Snapshot().Should().Be(avant);                         // aucune écriture, même sur les routes d'écriture
    }

    [Fact]
    public async Task AuthMe_SalarieAccount_ReturnsOnlyOwnAccount()
    {
        // Arrange
        using var actif = _f.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("SelfService:Enabled", "true") })));
        var http = actif.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {JetonSalarie()}");

        // Act
        var resp = await http.SendAsync(req);
        var texte = await resp.Content.ReadAsStringAsync();

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(texte);
        var donnees = json.RootElement.GetProperty("data");
        donnees.GetProperty("login").GetString().Should().Be("enum-salarie");       // son propre compte
        donnees.GetProperty("token").GetString().Should().Be(string.Empty);          // aucun jeton renvoyé
        donnees.GetProperty("rights").GetArrayLength().Should().Be(0);               // aucune porte
        texte.Should().NotContain("shifts.read");
    }
}
