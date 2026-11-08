using AutoMapper;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>Étape 4b-5 : cache de 30 secondes des droits effectifs, invalidation immédiate, échec fermé.</summary>
public class EffectiveRightsCacheTests
{
    private readonly InMemoryUnitOfWork _store = new();
    private readonly FakeTime _time = new();
    private readonly EffectiveRightsCache _cache;
    private readonly EffectiveRightsService _rights;
    private readonly AuthServices _auth;

    public EffectiveRightsCacheTests()
    {
        _cache = new EffectiveRightsCache(_time);
        _rights = new EffectiveRightsService(_store, _cache, Microsoft.Extensions.Logging.Abstractions.NullLogger<EffectiveRightsService>.Instance);
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthServices(_store, new Token(), mapper, new Mail(), new AdminCurrentUser(), _rights, new PasswordHasher<Responsable>());

        _store.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _store.Get<Role>().Items.Add(new Role { IdRole = 2, NomRole = "Utilisateur" });
        _store.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin" });
        _store.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 5, IdRole = 2, Login = "u5" });
        _store.Get<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = "BASE1" });
        _store.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 2, IdDroit = 1 });
        _store.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 2, PermissionCode = "SAGE:BASE1:READ", IsGranted = true });
        _store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = 1, IdResponsable = 5, IdBdd = 7 });
    }

    private async Task<bool> CanUse() => (await _rights.GetAsync(5)).CanUseDatabase(DatabaseKind.Sage, "BASE1");

    [Fact]
    public async Task Rights_are_read_once_then_served_from_cache()
    {
        Assert.True(await CanUse());
        _store.Get<BddAutorisee>().Items.Clear();        // changement direct en base, sans invalidation
        Assert.True(await CanUse());                            // toujours servi par le cache
    }

    [Fact]
    public async Task Cache_expires_after_30_seconds()
    {
        Assert.True(await CanUse());
        _store.Get<BddAutorisee>().Items.Clear();
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.True(await CanUse());
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(await CanUse());                           // relu en base : accès retiré
    }

    [Fact]
    public async Task Saving_database_access_invalidates_the_account_immediately()
    {
        Assert.True(await CanUse());
        await _auth.SetDbAccessAsync(5, Array.Empty<int>());
        Assert.False(await CanUse());
    }

    [Fact]
    public async Task Saving_user_rights_invalidates_the_account_immediately()
    {
        Assert.True(await CanUse());
        // Liste complète sans SAGE:BASE1:READ : la lecture est retirée.
        await _auth.SaveUserDetailedPermissionsAsync(1, 5, new[] { "REPORTS:REPORTS", "SAGE:CREATE" });
        Assert.False(await CanUse());
        await _auth.ResetUserDetailedPermissionsAsync(1, 5);
        Assert.True(await CanUse());                            // retour aux droits du rôle
    }

    [Fact]
    public async Task Saving_role_rights_invalidates_every_account()
    {
        Assert.True(await CanUse());
        await _auth.SaveRoleDetailedPermissionsAsync(1, 2, new[] { "REPORTS:REPORTS" });
        Assert.False(await CanUse());
    }

    [Fact]
    public async Task Changing_the_role_of_an_account_invalidates_it()
    {
        Assert.True(await CanUse());
        _store.Get<Role>().Items.Add(new Role { IdRole = 3, NomRole = "Sans droit" });
        await _auth.SaveUserAsync(1, new SoftTime.Application.DTOs.UserDto(5, 3, "u5", "u5", null, null));
        Assert.False(await CanUse());
    }

    [Fact]
    public async Task Deleting_an_account_invalidates_it_and_removes_its_access()
    {
        Assert.True(await CanUse());
        await _auth.DeleteUserAsync(1, 5);
        Assert.False(await CanUse());                           // compte disparu : plus aucun droit
        Assert.DoesNotContain(_store.Get<BddAutorisee>().Items, a => a.IdResponsable == 5);
    }

    [Fact]
    public async Task Fail_closed_when_rights_cannot_be_read_and_nothing_sensitive_is_logged()
    {
        var logger = new CapturingLogger<EffectiveRightsService>();
        var failing = new FailingUnitOfWork(_store);
        var rights = new EffectiveRightsService(failing, new EffectiveRightsCache(_time), logger);
        var tenant = new TenantConnectionService(failing, new Company("BASE1"), new TestCurrentUser(5, 2, 1), rights,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);

        failing.Fail = true;
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => tenant.EnsureAuthorizedAsync());
        Assert.Equal(TenantConnectionService.AccessDenied, ex.Message);
        Assert.DoesNotContain("SECRET-CONNECTION-STRING", ex.Message);

        // Journal : identifiant du compte et type d'erreur seulement, jamais le message de l'exception.
        Assert.Contains(logger.Lines, l => l.Contains("5") && l.Contains(nameof(InvalidOperationException)));
        Assert.DoesNotContain(logger.Lines, l => l.Contains("SECRET-CONNECTION-STRING") || l.Contains("Password=") || l.Contains("EXCEPTION"));

        // Une fois la lecture rétablie, l'accès revient.
        failing.Fail = false;
        await tenant.EnsureAuthorizedAsync();
    }

    [Fact]
    public async Task Anonymous_account_is_refused()
    {
        var tenant = new TenantConnectionService(_store, new Company("BASE1"), new NoUser(), _rights,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
        _store.Get<BddSage>().Items.Single().NomBd = "BASE1";
        await Assert.ThrowsAsync<ForbiddenException>(() => tenant.EnsureAuthorizedAsync());
    }

    private sealed class NoUser : ICurrentUser
    {
        public decimal? UserId => null;
        public string? Login => null;
        public decimal? RoleId => null;
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights => Array.Empty<int>();
        public bool HasRight(int droit) => false;
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class Company : ICompanyContext
    {
        public Company(string sage) => SageDatabase = sage;
        public string? SageDatabase { get; private set; }
        public string? PointeuseDatabase { get; private set; }
        public void Set(string sage, string? pointeuse) { SageDatabase = sage; PointeuseDatabase = pointeuse; }
    }

    /// <summary>Unité de travail qui échoue à la lecture des comptes quand Fail est vrai (message volontairement sensible).</summary>
    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        private readonly InMemoryUnitOfWork _inner;
        public FailingUnitOfWork(InMemoryUnitOfWork inner) => _inner = inner;
        public bool Fail { get; set; }

        public IRepository<T> Repository<T>() where T : class
        {
            if (Fail && typeof(T) == typeof(Responsable))
                throw new InvalidOperationException("SECRET-CONNECTION-STRING Password=xxx");
            return _inner.Repository<T>();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : "|EXCEPTION"));
    }

    private sealed class Token : ITokenService
    {
        public string CreateToken(TokenSubject subject) => "FAKE_TOKEN";
    }

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(AccountMailRecipient destinataire, string? password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(AccountMailRecipient destinataire, string? newPassword, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
