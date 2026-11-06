using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Tests.Http;

/// <summary>
/// Hôte de test en mémoire : jetons fabriqués avec une clé de test, aucune base réelle
/// (IUnitOfWork et les services externes sont remplacés par des doubles).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestKey = "test-only-signing-key-0123456789-abcdefghijklmnop";
    public const string TestIssuer = "SoftTime.Tests";
    public const string TestAudience = "SoftTime.Tests.Web";
    public const string FakeSagePassword = "FAKE-SAGE-SECRET-PWD";
    public const string FakePointeusePassword = "FAKE-PTE-SECRET-PWD";
    public const string SageName = "SFOI_PAIE2025";
    public const string OtherSageName = "AUTRE_BASE";

    public static readonly string ReportPath = Path.Combine(Path.GetTempPath(), "softtime-http-report.txt");

    public SnapshotInMemoryUnitOfWork Store { get; } = new();

    static ApiFactory()
    {
        // Portée : processus de test uniquement, jamais écrit dans un fichier.
        // Les variables d'environnement l'emportent sur appsettings.json, qui n'est pas lu par les tests.
        Environment.SetEnvironmentVariable("Jwt__Key", TestKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
        // Moteur prédictif : pas de tâche de fond ni de base analytique dans les tests HTTP.
        Environment.SetEnvironmentVariable("Ml__Enabled", "false");
        // Aucune migration de schéma : la base est simulée en mémoire, quel que soit le appsettings.json local du poste.
        Environment.SetEnvironmentVariable("Database__AutoMigrate", "false");
        Environment.SetEnvironmentVariable("ConnectionStrings__SoftTime",
            "Data Source=invalid.host.test;Initial Catalog=INVALID;User ID=none;Password=none;Connect Timeout=1");
    }

    public ApiFactory()
    {
        // Plusieurs classes de test créent leur fabrique en parallèle : même verrou que Report.
        lock (ReportPath) File.WriteAllText(ReportPath, string.Empty);
        Seed();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton<IUnitOfWork>(Store);
            services.RemoveAll<IExternalDiscoveryService>();
            services.AddSingleton(NullObject.Create<IExternalDiscoveryService>());
            services.RemoveAll<SoftTime.Application.Abstractions.IMlStore>();
            services.AddSingleton(NullObject.Create<SoftTime.Application.Abstractions.IMlStore>());
            services.RemoveAll<IExternalSourceService>();
            services.AddSingleton(NullObject.Create<IExternalSourceService>());
        });
    }

    private void Seed()
    {
        var roles = Store.GetRepo<T_ROLE>().Items;
        roles.Add(new T_ROLE { IDROLE = 1, NOMROLE = "Administrateur" });
        roles.Add(new T_ROLE { IDROLE = 29, NOMROLE = "Consultation" });
        roles.Add(new T_ROLE { IDROLE = 40, NOMROLE = "Gestionnaire" });
        roles.Add(new T_ROLE { IDROLE = 90, NOMROLE = "Jetable" });
        roles.Add(new T_ROLE { IDROLE = 91, NOMROLE = "Cible" });
        roles.Add(new T_ROLE { IDROLE = 10034, NOMROLE = "Utilisateur ordinaire" });

        var users = Store.GetRepo<T_RESPONSABLE>().Items;
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = 14, IDROLE = 1, LOGIN = "admin", NOMRESPONSABLE = "Admin" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = 10034, IDROLE = 10034, LOGIN = "joel", NOMRESPONSABLE = "Joel" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = 10030, IDROLE = 29, LOGIN = "user1", NOMRESPONSABLE = "User1" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = 10040, IDROLE = 40, LOGIN = "param2", NOMRESPONSABLE = "Param2" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = 10050, IDROLE = 91, LOGIN = "cible", NOMRESPONSABLE = "Cible" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = 10051, IDROLE = 91, LOGIN = "jetable", NOMRESPONSABLE = "Jetable" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = 10052, IDROLE = 91, LOGIN = "modifiable", NOMRESPONSABLE = "Modifiable" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = 10053, IDROLE = 91, LOGIN = "supprimable", NOMRESPONSABLE = "Supprimable" });

        // Profil « comme joel » : portes 1 et 3 via le jeton, permissions fines de lecture seulement.
        var perms = Store.GetRepo<T_USER_PERMISSION>().Items;
        foreach (var code in new[] { $"SAGE:{SageName}:READ", "POINTEUSE:PTG:READ", "PROC:SHIFTS", "PROC:EMPLOYEE_PLANNING" })
            perms.Add(new T_USER_PERMISSION { IDRESPONSABLE = 10034, PERMISSION_CODE = code, IS_GRANTED = true });
        Store.GetRepo<T_ROLE_PERMISSION>().Items.Add(new T_ROLE_PERMISSION { IDROLE = 29, PERMISSION_CODE = "REPORTS:REPORTS", IS_GRANTED = true });

        var sages = Store.GetRepo<T_BDD_SAGE>().Items;
        sages.Add(new T_BDD_SAGE { ID = 3033, NOM_BD = SageName, SERVEUR = "invalid.host.test", TLOGIN = "x", TMDP = FakeSagePassword, TYPE_AUTH = true });
        sages.Add(new T_BDD_SAGE { ID = 3034, NOM_BD = OtherSageName, SERVEUR = "invalid.host.test", TLOGIN = "x", TMDP = FakeSagePassword, TYPE_AUTH = true });
        sages.Add(new T_BDD_SAGE { ID = 3036, NOM_BD = "MODIF_SAGE", SERVEUR = "invalid.host.test", TLOGIN = "x", TMDP = FakeSagePassword, TYPE_AUTH = true });
        sages.Add(new T_BDD_SAGE { ID = 3035, NOM_BD = "JETABLE_SAGE", SERVEUR = "invalid.host.test", TLOGIN = "x", TMDP = FakeSagePassword, TYPE_AUTH = true });
        var pointeuses = Store.GetRepo<T_BDD_POINTEUSE>().Items;
        pointeuses.Add(new T_BDD_POINTEUSE { ID = 1, NOM_BD = "PTG", SERVEUR = "invalid.host.test", TLOGIN = "x", TMDP = FakePointeusePassword, TYPE_AUTH = true, ACTIVE = true });
        pointeuses.Add(new T_BDD_POINTEUSE { ID = 3, NOM_BD = "MODIF_PTG", SERVEUR = "invalid.host.test", TLOGIN = "x", TMDP = FakePointeusePassword, TYPE_AUTH = true });
        pointeuses.Add(new T_BDD_POINTEUSE { ID = 2, NOM_BD = "JETABLE_PTG", SERVEUR = "invalid.host.test", TLOGIN = "x", TMDP = FakePointeusePassword, TYPE_AUTH = true });

        // Portes du rôle « Utilisateur ordinaire » (1 et 3), comme le jeton de test de joel.
        Store.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDDROITROLE = 1, IDROLE = 10034, IDDROIT = 1 });
        Store.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDDROITROLE = 2, IDROLE = 10034, IDDROIT = 3 });
        Store.GetRepo<T_BDD_AUTORISE>().Items.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = 1, IDRESPONSABLE = 14, IDBDD = 3033 });
        Store.GetRepo<T_BDD_AUTORISE>().Items.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = 2, IDRESPONSABLE = 10053, IDBDD = 3033 });
        foreach (var code in new[] { "PROC:SHIFTS", "SAGE:SFOI_PAIE2025:READ" })
            perms.Add(new T_USER_PERMISSION { IDRESPONSABLE = 10053, PERMISSION_CODE = code, IS_GRANTED = true });

        // Étape 4b-5 : usage d'une base. joel a sa base cochée dans « Accès bases » (comme après la migration des données).
        var access = Store.GetRepo<T_BDD_AUTORISE>().Items;
        access.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = 3, IDRESPONSABLE = 10034, IDBDD = 3033 });
        Store.GetRepo<T_CLOCK>().Items.Add(new T_CLOCK { IDPOINT = 1 });
        void Account(decimal id, decimal role, string login, bool linked, params string[] codes)
        {
            users.Add(new T_RESPONSABLE { IDRESPONSABLE = id, IDROLE = role, LOGIN = login, NOMRESPONSABLE = login });
            if (linked) access.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = (int)id, IDRESPONSABLE = id, IDBDD = 3033 });
            foreach (var code in codes)
                perms.Add(new T_USER_PERMISSION { IDRESPONSABLE = id, PERMISSION_CODE = code, IS_GRANTED = true });
        }
        Account(10060, 10034, "nolink", false, $"SAGE:{SageName}:READ", "POINTEUSE:PTG:READ", "PROC:SHIFTS");        // base non cochée
        Account(10061, 10034, "noread", true, "POINTEUSE:PTG:READ", "PROC:SHIFTS");                                   // cochée, sans lecture
        Account(10062, 91, "nogate", true, "PROC:SHIFTS");                                                            // aucun droit sur les bases (pas de porte 1)
        Account(10063, 10034, "ptenone", true, $"SAGE:{SageName}:READ", "PROC:SHIFTS");                               // sans droit pointeuse
        // Étape 4b-9 : porte 2 (déduite de PARAMS:*) avec une seule permission fine, pour les lectures sensibles.
        Account(10070, 91, "usersreader", false, "PARAMS:USERS");
        Account(10071, 91, "rolesreader", false, "PARAMS:ROLES");
        Account(10072, 91, "privreader", false, "PARAMS:PRIVILEGES");
        Account(10073, 91, "dbreader", false, "PARAMS:DB_ACCESS");
        Account(10074, 91, "paramsother", false, "PARAMS:CATEGORIES");
        // Prévisions : porte 6 avec la permission fine, et porte 6 sans elle.
        Account(10090, 91, "predreader", true, "REPORTS:PREDICTIONS", $"SAGE:{SageName}:READ");
        Account(10092, 91, "prednobase", false, "REPORTS:PREDICTIONS");                  // permission fine mais base non cochée
        Account(10091, 91, "gate6only", true, "REPORTS:REPORTS", $"SAGE:{SageName}:READ");
        // Étape 4b-8 : comptes avec ajustements personnels (rôle 91 sans droits : chaque ligne est un ajout).
        Account(10080, 91, "adjusted1", false, "PROC:SHIFTS", "PROC:PUNCHES");
        Account(10081, 91, "adjusted2", false, "PROC:SHIFTS", "PROC:PUNCHES");
        Account(10083, 91, "adjusted3", false, "PROC:SHIFTS", "PROC:PUNCHES");
    }

    public static string Token(decimal userId, decimal roleId, params int[] rights)
    {
        var uid = ((long)userId).ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, uid),
            new(ClaimTypes.NameIdentifier, uid),
            new("uid", uid),
            new(ClaimTypes.Name, $"test{uid}"),
            new("roleId", ((long)roleId).ToString()),
            new("matricule", string.Empty),
            new("rights", string.Join(",", rights))
        };
        var token = new JwtSecurityToken(
            TestIssuer, TestAudience, claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Profils de test : null = anonyme.</summary>
    public static string? TokenFor(string profile) => profile switch
    {
        "anonyme" => null,
        "admin" => Token(14, 1, 1, 2, 3, 4, 5, 6),
        "joel" => Token(10034, 10034, 1, 3),
        "user1" => Token(10030, 29, 6),
        "param2" => Token(10040, 40, 2),
        "nolink" => Token(10060, 10034, 1, 3),
        "noread" => Token(10061, 10034, 1, 3),
        "nogate" => Token(10062, 91, 3),
        "ptenone" => Token(10063, 10034, 1, 3),
        "usersreader" => Token(10070, 91, 2),
        "rolesreader" => Token(10071, 91, 2),
        "privreader" => Token(10072, 91, 2),
        "dbreader" => Token(10073, 91, 2),
        "paramsother" => Token(10074, 91, 2),
        "predreader" => Token(10090, 91, 1, 6),
        "prednobase" => Token(10092, 91, 6),
        "gate6only" => Token(10091, 91, 1, 6),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    public HttpRequestMessage Request(string profile, HttpMethod method, string url, string? body = null, string? sageHeader = null, string? pointeuseHeader = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (TokenFor(profile) is string t)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {t}");
        if (sageHeader is not null)
            req.Headers.TryAddWithoutValidation("X-Sage-Database", sageHeader);
        if (pointeuseHeader is not null)
            req.Headers.TryAddWithoutValidation("X-Pointeuse-Database", pointeuseHeader);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return req;
    }

    public static void Report(string line)
    {
        lock (ReportPath) File.AppendAllText(ReportPath, line + Environment.NewLine);
    }

    /// <summary>Instantané sérialisé de toutes les données en mémoire, pour prouver l'absence d'écriture.</summary>
    public string Snapshot() => Store.Snapshot();
}
