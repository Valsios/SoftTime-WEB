using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.ExternalData;
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

    public InMemoryUnitOfWork Store { get; } = new();

    static ApiFactory()
    {
        // Portée : processus de test uniquement, jamais écrit dans un fichier.
        // Les variables d'environnement l'emportent sur appsettings.json, qui n'est pas lu par les tests.
        Environment.SetEnvironmentVariable("Jwt__Key", TestKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
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
        MarkSeededRowsAsLegacyCopies();
        // Démarre l'hôte tout de suite : la reprise de la migration 006 (au démarrage) a lieu avant le premier test,
        // pas au milieu d'un test qui compare deux instantanés du magasin.
        _ = Services;
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
            services.RemoveAll<IExternalSourceService>();
            services.AddSingleton(NullObject.Create<IExternalSourceService>());
        });
    }

    private void Seed()
    {
        var roles = Store.Get<Role>().Items;
        roles.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        roles.Add(new Role { IdRole = 29, NomRole = "Consultation" });
        roles.Add(new Role { IdRole = 40, NomRole = "Gestionnaire" });
        roles.Add(new Role { IdRole = 90, NomRole = "Jetable" });
        roles.Add(new Role { IdRole = 91, NomRole = "Cible" });
        roles.Add(new Role { IdRole = 10034, NomRole = "Utilisateur ordinaire" });

        var users = Store.Get<Responsable>().Items;
        users.Add(new Responsable { IdResponsable = 14, IdRole = 1, Login = "admin", NomResponsable = "Admin" });
        users.Add(new Responsable { IdResponsable = 10034, IdRole = 10034, Login = "joel", NomResponsable = "Joel" });
        users.Add(new Responsable { IdResponsable = 10030, IdRole = 29, Login = "user1", NomResponsable = "User1" });
        users.Add(new Responsable { IdResponsable = 10040, IdRole = 40, Login = "param2", NomResponsable = "Param2" });
        users.Add(new Responsable { IdResponsable = 10050, IdRole = 91, Login = "cible", NomResponsable = "Cible" });
        users.Add(new Responsable { IdResponsable = 10051, IdRole = 91, Login = "jetable", NomResponsable = "Jetable" });
        users.Add(new Responsable { IdResponsable = 10052, IdRole = 91, Login = "modifiable", NomResponsable = "Modifiable" });
        users.Add(new Responsable { IdResponsable = 10053, IdRole = 91, Login = "supprimable", NomResponsable = "Supprimable" });

        // Profil « comme joel » : portes 1 et 3 via le jeton, permissions fines de lecture seulement.
        var perms = Store.Get<UserPermission>().Items;
        foreach (var code in new[] { $"SAGE:{SageName}:READ", "POINTEUSE:PTG:READ", "PROC:SHIFTS", "PROC:EMPLOYEE_PLANNING" })
            perms.Add(new UserPermission { IdResponsable = 10034, PermissionCode = code, IsGranted = true });
        Store.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 29, PermissionCode = "REPORTS:REPORTS", IsGranted = true });
        // Lot 5 : param2 (jeton porte 2) a la porte 2 en base grâce à une permission sans rapport avec les écrans sensibles.
        perms.Add(new UserPermission { IdResponsable = 10040, PermissionCode = "PARAMS:CLOCK_PARAMS", IsGranted = true });

        var sages = Store.Get<BddSage>().Items;
        sages.Add(new BddSage { Id = 3033, NomBd = SageName, Serveur = "invalid.host.test", TLogin = "x", TMdp = FakeSagePassword, TypeAuth = true });
        sages.Add(new BddSage { Id = 3034, NomBd = OtherSageName, Serveur = "invalid.host.test", TLogin = "x", TMdp = FakeSagePassword, TypeAuth = true });
        sages.Add(new BddSage { Id = 3036, NomBd = "MODIF_SAGE", Serveur = "invalid.host.test", TLogin = "x", TMdp = FakeSagePassword, TypeAuth = true });
        sages.Add(new BddSage { Id = 3035, NomBd = "JETABLE_SAGE", Serveur = "invalid.host.test", TLogin = "x", TMdp = FakeSagePassword, TypeAuth = true });
        var pointeuses = Store.Get<BddPointeuse>().Items;
        pointeuses.Add(new BddPointeuse { Id = 1, NomBd = "PTG", Serveur = "invalid.host.test", TLogin = "x", TMdp = FakePointeusePassword, TypeAuth = true, Active = true });
        pointeuses.Add(new BddPointeuse { Id = 3, NomBd = "MODIF_PTG", Serveur = "invalid.host.test", TLogin = "x", TMdp = FakePointeusePassword, TypeAuth = true });
        pointeuses.Add(new BddPointeuse { Id = 2, NomBd = "JETABLE_PTG", Serveur = "invalid.host.test", TLogin = "x", TMdp = FakePointeusePassword, TypeAuth = true });

        // Portes du rôle « Utilisateur ordinaire » (1 et 3), comme le jeton de test de joel.
        Store.Get<DroitRole>().Items.Add(new DroitRole { IdDroitRole = 1, IdRole = 10034, IdDroit = 1 });
        Store.Get<DroitRole>().Items.Add(new DroitRole { IdDroitRole = 2, IdRole = 10034, IdDroit = 3 });
        Store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = 1, IdResponsable = 14, IdBdd = 3033 });
        Store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = 2, IdResponsable = 10053, IdBdd = 3033 });
        foreach (var code in new[] { "PROC:SHIFTS", "SAGE:SFOI_PAIE2025:READ" })
            perms.Add(new UserPermission { IdResponsable = 10053, PermissionCode = code, IsGranted = true });

        // Étape 4b-5 : usage d'une base. joel a sa base cochée dans « Accès bases » (comme après la migration des données).
        var access = Store.Get<BddAutorisee>().Items;
        access.Add(new BddAutorisee { IdBddAutorise = 3, IdResponsable = 10034, IdBdd = 3033 });
        Store.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { IdPoint = 1 });
        void Account(decimal id, decimal role, string login, bool linked, params string[] codes)
        {
            users.Add(new Responsable { IdResponsable = id, IdRole = role, Login = login, NomResponsable = login });
            if (linked) access.Add(new BddAutorisee { IdBddAutorise = (int)id, IdResponsable = id, IdBdd = 3033 });
            foreach (var code in codes)
                perms.Add(new UserPermission { IdResponsable = id, PermissionCode = code, IsGranted = true });
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
        // L'atterrissage se calcule sur les pointages : la pointeuse doit être lisible elle aussi.
        Account(10090, 91, "predreader", true, "REPORTS:PREDICTIONS", $"SAGE:{SageName}:READ", "POINTEUSE:PTG:READ");
        Account(10092, 91, "prednobase", false, "REPORTS:PREDICTIONS");                  // permission fine mais base non cochée
        Account(10093, 91, "prednopte", true, "REPORTS:PREDICTIONS", $"SAGE:{SageName}:READ");   // sans droit pointeuse
        Account(10091, 91, "gate6only", true, "REPORTS:REPORTS", $"SAGE:{SageName}:READ");
        // Étape 4b-8 : comptes avec ajustements personnels (rôle 91 sans droits : chaque ligne est un ajout).
        Account(10080, 91, "adjusted1", false, "PROC:SHIFTS", "PROC:PUNCHES");
        Account(10081, 91, "adjusted2", false, "PROC:SHIFTS", "PROC:PUNCHES");
        Account(10083, 91, "adjusted3", false, "PROC:SHIFTS", "PROC:PUNCHES");
    }

    /// <summary>Lignes amorcées par Seed : copies complètes d'avant la migration 006 (converties au démarrage de l'hôte).</summary>
    private void MarkSeededRowsAsLegacyCopies()
    {
        foreach (var row in Store.Get<UserPermission>().Items)
            row.IsDeviation = false;
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
        "prednopte" => Token(10093, 91, 1, 6),
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

    /// <summary>Vide le magasin en mémoire et rejoue les données initiales.</summary>
    public void ResetStore()
    {
        Store.Clear();
        Seed();
        MarkSeededRowsAsLegacyCopies();
        // L'hôte est déjà démarré : la reprise de la migration 006 est rejouée sur les lignes réamorcées.
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserPermissionDeviationMigration>().RunAsync().GetAwaiter().GetResult();
    }
}
