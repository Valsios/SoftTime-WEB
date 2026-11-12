using AutoMapper;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Abstractions.Notifications;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services.Permissions;
using SoftTime.Infrastructure.Services.RequestContext;
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
        _store.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 2, PermissionCode = "sage.7.read", IsGranted = true });
        _store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = 1, IdResponsable = 5, IdBdd = 7 });
    }

    private async Task<bool> CanUse() => (await _rights.GetAsync(5)).CanUseDatabase(DatabaseKind.Sage, "BASE1");

    [Fact]
    public async Task CanUse_RepeatedCalls_ReadsRightsOnceThenUsesCache()
    {
        // Act & Assert
        (await CanUse()).Should().BeTrue();
        _store.Get<BddAutorisee>().Items.Clear();
        (await CanUse()).Should().BeTrue();
    }

    [Fact]
    public async Task CanUse_After30Seconds_ReloadsRights()
    {
        // Act & Assert
        (await CanUse()).Should().BeTrue();
        _store.Get<BddAutorisee>().Items.Clear();
        _time.Advance(TimeSpan.FromSeconds(29));
        (await CanUse()).Should().BeTrue();
        _time.Advance(TimeSpan.FromSeconds(2));
        (await CanUse()).Should().BeFalse();
    }

    [Fact]
    public async Task CanUse_DatabaseAccessSaved_InvalidatesAccountImmediately()
    {
        // Act & Assert
        (await CanUse()).Should().BeTrue();
        await _auth.SetDbAccessAsync(5, Array.Empty<int>());
        (await CanUse()).Should().BeFalse();
    }

    [Fact]
    public async Task CanUse_UserRightsSaved_InvalidatesAccountImmediately()
    {
        // Act & Assert
        (await CanUse()).Should().BeTrue();
        // Liste complète sans sage.7.read : la lecture est retirée.
        await _auth.SaveUserDetailedPermissionsAsync(1, 5, new[] { "reports.read", "sage.create" });
        (await CanUse()).Should().BeFalse();
        await _auth.ResetUserDetailedPermissionsAsync(1, 5);
        (await CanUse()).Should().BeTrue();
    }

    [Fact]
    public async Task CanUse_RoleRightsSaved_InvalidatesEveryAccount()
    {
        // Act & Assert
        (await CanUse()).Should().BeTrue();
        await _auth.SaveRoleDetailedPermissionsAsync(1, 2, new[] { "reports.read" });
        (await CanUse()).Should().BeFalse();
    }

    [Fact]
    public async Task CanUse_AccountRoleChanged_InvalidatesAccount()
    {
        // Act & Assert
        (await CanUse()).Should().BeTrue();
        _store.Get<Role>().Items.Add(new Role { IdRole = 3, NomRole = "Sans droit" });
        await _auth.SaveUserAsync(1, new SoftTime.Application.DTOs.UserDto(5, 3, "u5", "u5", null, null));
        (await CanUse()).Should().BeFalse();
    }

    [Fact]
    public async Task CanUse_AccountDeleted_InvalidatesAndRemovesAccess()
    {
        // Act & Assert
        (await CanUse()).Should().BeTrue();
        await _auth.DeleteUserAsync(1, 5);
        (await CanUse()).Should().BeFalse();
        _store.Get<BddAutorisee>().Items.Should().NotContain(a => a.IdResponsable == 5);
    }

    [Fact]
    public async Task EnsureAuthorizedAsync_RightsUnreadable_FailsClosedWithoutLoggingSecrets()
    {
        // Arrange
        var logger = new CapturingLogger<EffectiveRightsService>();
        var failing = new FailingUnitOfWork(_store);
        var rights = new EffectiveRightsService(failing, new EffectiveRightsCache(_time), logger);
        var sut = new TenantConnectionService(failing, new Company("BASE1"), new TestCurrentUser(5, 2, 1), rights,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
        failing.Fail = true;

        // Act
        Func<Task> act = () => sut.EnsureAuthorizedAsync();

        // Assert
        var ex = (await act.Should().ThrowExactlyAsync<ForbiddenException>()).Which;
        ex.Message.Should().Be(TenantConnectionService.AccessDenied);
        ex.Message.Should().NotContain("SECRET-CONNECTION-STRING");
        // Journal : identifiant du compte et type d'erreur seulement, jamais le message de l'exception.
        logger.Lines.Should().Contain(l => l.Contains("5") && l.Contains(nameof(InvalidOperationException)));
        logger.Lines.Should().NotContain(l => l.Contains("SECRET-CONNECTION-STRING") || l.Contains("Password=") || l.Contains("EXCEPTION"));
        // Une fois la lecture rétablie, l'accès revient.
        failing.Fail = false;
        await sut.EnsureAuthorizedAsync();
    }

    [Fact]
    public async Task EnsureAuthorizedAsync_AnonymousAccount_IsRefused()
    {
        // Arrange
        var sut = new TenantConnectionService(_store, new Company("BASE1"), new NoUser(), _rights,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
        _store.Get<BddSage>().Items.Single().NomBd = "BASE1";

        // Act
        Func<Task> act = () => sut.EnsureAuthorizedAsync();

        // Assert
        await act.Should().ThrowExactlyAsync<ForbiddenException>();
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
