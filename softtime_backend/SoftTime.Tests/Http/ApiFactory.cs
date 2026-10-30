using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

    public InMemoryUnitOfWork Store { get; } = new();

    static ApiFactory()
    {
        // Portée : processus de test uniquement, jamais écrit dans un fichier.
        // Les variables d'environnement l'emportent sur appsettings.json, qui n'est pas lu par les tests.
        Environment.SetEnvironmentVariable("Jwt__Key", TestKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
        Environment.SetEnvironmentVariable("ConnectionStrings__SoftTime",
            "Data Source=invalid.host.test;Initial Catalog=INVALID;User ID=none;Password=none;Connect Timeout=1");
    }

    public ApiFactory()
    {
        File.WriteAllText(ReportPath, string.Empty);
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
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    public HttpRequestMessage Request(string profile, HttpMethod method, string url, string? body = null, string? sageHeader = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (TokenFor(profile) is string t)
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {t}");
        if (sageHeader is not null)
            req.Headers.TryAddWithoutValidation("X-Sage-Database", sageHeader);
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

public sealed class InMemoryRepository<T> : IRepository<T> where T : class
{
    public List<T> Items { get; } = new();

    public IQueryable<T> Query() => Items.AsQueryable();

    public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        var d = Convert.ToDecimal(id);
        var match = Items.FirstOrDefault(i => i switch
        {
            T_RESPONSABLE r => r.IDRESPONSABLE == d,
            T_ROLE ro => ro.IDROLE == d,
            T_BDD_SAGE s => s.ID == d,
            T_BDD_POINTEUSE p => p.ID == d,
            _ => false
        });
        return Task.FromResult(match);
    }

    public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Where(predicate.Compile()).ToList());

    public Task<List<T>> ListLatestAsync(Expression<Func<T, bool>> predicate, Expression<Func<T, DateTime?>> orderByDescending, int take, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.Where(predicate.Compile()).OrderByDescending(orderByDescending.Compile()).Take(take).ToList());

    public Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        if (entity is T_RESPONSABLE r && r.IDRESPONSABLE == 0)
            r.IDRESPONSABLE = Items.OfType<T_RESPONSABLE>().Select(x => x.IDRESPONSABLE).DefaultIfEmpty(0).Max() + 1;
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        Items.AddRange(entities);
        return Task.CompletedTask;
    }

    public void Update(T entity) { }
    public void Remove(T entity) => Items.Remove(entity);
    public void RemoveRange(IEnumerable<T> entities)
    {
        foreach (var e in entities.ToList()) Items.Remove(e);
    }
}

public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repos = new();
    private readonly object _gate = new();

    public InMemoryRepository<T> GetRepo<T>() where T : class
    {
        lock (_gate)
        {
            if (!_repos.TryGetValue(typeof(T), out var r))
                _repos[typeof(T)] = r = new InMemoryRepository<T>();
            return (InMemoryRepository<T>)r;
        }
    }

    public IRepository<T> Repository<T>() where T : class => GetRepo<T>();
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public string Snapshot()
    {
        var options = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
        var sb = new StringBuilder();
        lock (_gate)
        {
            foreach (var kv in _repos.OrderBy(k => k.Key.Name).Where(k => ((System.Collections.ICollection)k.Value.GetType().GetProperty("Items")!.GetValue(k.Value)!).Count > 0))
            {
                var items = kv.Value.GetType().GetProperty("Items")!.GetValue(kv.Value)!;
                sb.Append(kv.Key.Name).Append('=').Append(JsonSerializer.Serialize(items, items.GetType(), options)).Append('\n');
            }
        }
        return sb.ToString();
    }
}

/// <summary>Double « objet nul » : toute méthode renvoie une valeur vide, aucune connexion externe.</summary>
public class NullObject : DispatchProxy
{
    public static T Create<T>() where T : class => Create<T, NullObject>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var rt = targetMethod!.ReturnType;
        if (rt == typeof(void)) return null;
        if (rt == typeof(Task)) return Task.CompletedTask;
        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = rt.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { Empty(inner) });
        }
        return Empty(rt);
    }

    private static object? Empty(Type t)
    {
        if (t.IsValueType) return Activator.CreateInstance(t);
        if (t == typeof(string)) return string.Empty;
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(IReadOnlyList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyCollection<>)
                || def == typeof(IList<>) || def == typeof(ICollection<>))
                return Array.CreateInstance(t.GetGenericArguments()[0], 0);
        }
        return null;
    }
}
