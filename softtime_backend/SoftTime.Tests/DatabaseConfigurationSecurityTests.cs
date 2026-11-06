using System.Linq.Expressions;

using Microsoft.AspNetCore.Identity;

using AutoMapper;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

public class DatabaseConfigurationSecurityTests
{
    private const string Base = "BASE1";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
    private readonly AuthServices _auth;

    public DatabaseConfigurationSecurityTests()
    {
        _auth = new AuthServices(_uow, new FakeTokenService(), _mapper, new FakeEmailService(), new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.GetRepo<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Gestionnaire" });
        _uow.GetRepo<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = Base, Serveur = "srv" });
        _uow.GetRepo<BddPointeuse>().Items.Add(new BddPointeuse { Id = 8, NomBd = "PTG", Serveur = "srv" });
    }

    private DatabaseConfigurationAuthorizer AuthorizerFor(decimal userId, decimal roleId, params int[] rights)
    {
        if (!_uow.GetRepo<Responsable>().Items.Any(u => u.IdResponsable == userId))
            _uow.GetRepo<Responsable>().Items.Add(new Responsable { IdResponsable = userId, IdRole = roleId, Login = $"u{userId}" });
        return new DatabaseConfigurationAuthorizer(new FakeCurrentUser(userId, roleId, rights), _auth);
    }

    private void GrantUser(decimal userId, params string[] codes)
    {
        foreach (var c in codes)
            _uow.GetRepo<UserPermission>().Items.Add(new UserPermission { IdResponsable = userId, PermissionCode = c, IsGranted = true });
    }

    private void GrantRole(decimal roleId, params string[] codes)
    {
        foreach (var c in codes)
            _uow.GetRepo<RolePermission>().Items.Add(new RolePermission { IdRole = roleId, PermissionCode = c, IsGranted = true });
    }

    private static Task<ForbiddenException> Refused(Func<Task> act) => Assert.ThrowsAsync<ForbiddenException>(act);

    #region AdminAccess

    [Fact]
    public void AdminAccess_OnlyRoleOneIsAdmin()
    {
        Assert.True(AdminAccess.IsAdmin(new FakeCurrentUser(1, 1)));
        Assert.False(AdminAccess.IsAdmin(new FakeCurrentUser(2, 5, Rights.Parameters)));
        Assert.Throws<ForbiddenException>(() => AdminAccess.Require(new FakeCurrentUser(2, 5, Rights.Parameters)));
        AdminAccess.Require(new FakeCurrentUser(1, 1));
    }

    #endregion

    #region Configuration des bases

    [Fact]
    public async Task Admin_CanCreateUpdateDelete()
    {
        var a = AuthorizerFor(1, 1);
        await a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, "X");
        await a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, Base);
        await a.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Delete, "PTG");
        await a.EnsureCanConfigureAnyAsync(null);
    }

    [Fact]
    public async Task RoleWithoutRight_IsRefused()
    {
        var a = AuthorizerFor(20, 5, Rights.Reports);
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, "X"));
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, Base));
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Delete, "PTG"));
        await Refused(() => a.EnsureCanConfigureAnyAsync(null));
    }

    [Fact]
    public async Task RoleWithPermission_IsAllowedOnlyForGrantedAction()
    {
        GrantRole(5, $"SAGE:{Base}:UPDATE", $"SAGE:{Base}:READ");
        var a = AuthorizerFor(21, 5, Rights.Databases);
        await a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, Base);
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Delete, Base));
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, "X"));
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Update, "PTG"));
        await a.EnsureCanConfigureAnyAsync(DatabaseKind.Sage);
        await Refused(() => a.EnsureCanConfigureAnyAsync(DatabaseKind.Pointeuse));
    }

    [Fact]
    public async Task CustomizedAccountWithPermissionAndRight_IsAllowed()
    {
        GrantUser(22, "SAGE:CREATE", $"SAGE:{Base}:DELETE");
        var a = AuthorizerFor(22, 5, Rights.Databases);
        await a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, "X");
        await a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Delete, Base);
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, Base));
    }

    [Fact]
    public async Task CustomizedAccountWithPermissionButWithoutRight_IsRefused()
    {
        GrantUser(23, "SAGE:CREATE", $"SAGE:{Base}:UPDATE", $"SAGE:{Base}:DELETE");
        var a = AuthorizerFor(23, 5, Rights.Processing);
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, "X"));
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, Base));
    }

    [Fact]
    public async Task CustomizedAccountWithReadOnly_IsRefused()
    {
        GrantUser(24, $"SAGE:{Base}:READ", "POINTEUSE:PTG:READ");
        var a = AuthorizerFor(24, 5, Rights.Databases);
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, "X"));
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, Base));
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Delete, "PTG"));
        await Refused(() => a.EnsureCanConfigureAnyAsync(null));
    }

    [Fact]
    public async Task NonCustomizedAccountWithoutRight_IsRefused()
    {
        var a = AuthorizerFor(25, 5, Rights.Reports);
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Delete, Base));
    }

    [Fact]
    public async Task EmptyDatabaseName_IsRefusedForUpdateAndDelete()
    {
        GrantUser(26, "SAGE:CREATE", $"SAGE:{Base}:UPDATE", $"SAGE:{Base}:DELETE");
        var a = AuthorizerFor(26, 5, Rights.Databases);
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Delete, ""));
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, null));
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Delete, "  "));
    }

    [Fact]
    public async Task UnknownUser_IsRefused()
    {
        var a = new DatabaseConfigurationAuthorizer(new FakeCurrentUser(999, 5, Rights.Databases), _auth);
        await Refused(() => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, "X"));
    }

    #endregion

    #region CatalogService : refus avant toute écriture

    private CatalogServices CatalogFor(DatabaseConfigurationAuthorizer authorizer)
        => new(_uow, _mapper, null!, null!, null!, authorizer, SoftTime.Tests.Http.TestRights.Service(_uow));

    [Fact]
    public async Task CatalogService_DeleteSage_RefusedWithoutPermission_KeepsRow()
    {
        var svc = CatalogFor(AuthorizerFor(30, 5, Rights.Databases));
        GrantUser(30, $"SAGE:{Base}:READ");
        await Refused(() => svc.Sage.DeleteSageAsync(7));
        Assert.Single(_uow.GetRepo<BddSage>().Items);
    }

    [Fact]
    public async Task CatalogService_DeleteSage_EmptyName_IsRefused()
    {
        _uow.GetRepo<BddSage>().Items.Add(new BddSage { Id = 9, NomBd = "", Serveur = "srv" });
        GrantUser(31, "SAGE:CREATE", "SAGE::DELETE");
        var svc = CatalogFor(AuthorizerFor(31, 5, Rights.Databases));
        await Refused(() => svc.Sage.DeleteSageAsync(9));
        Assert.Equal(2, _uow.GetRepo<BddSage>().Items.Count);
    }

    [Fact]
    public async Task CatalogService_SaveSage_CreateAndUpdate_RefusedWithoutPermission()
    {
        var svc = CatalogFor(AuthorizerFor(32, 5, Rights.Reports));
        var dto = new SageDbDto(0, "srv", null, null, true, "NOUVELLE");
        await Refused(() => svc.Sage.SaveSageAsync(dto));
        await Refused(() => svc.Sage.SaveSageAsync(dto with { Id = 7 }));
        Assert.Single(_uow.GetRepo<BddSage>().Items);
    }

    [Fact]
    public async Task CatalogService_Pointeuse_SaveAndDelete_RefusedWithoutPermission()
    {
        var svc = CatalogFor(AuthorizerFor(33, 5, Rights.Reports));
        await Refused(() => svc.Pointeuse.DeletePointeuseAsync(8));
        await Refused(() => svc.Pointeuse.SavePointeuseAsync(new PointeuseDbDto(0, "srv", null, null, true, "NEW", null, false)));
        Assert.Single(_uow.GetRepo<BddPointeuse>().Items);
    }

    #endregion

    #region Rôle Administrateur intact

    [Fact]
    public async Task AdminRole_CannotBeRenamedDeletedOrHaveItsPrivilegesReplaced()
    {
        await Assert.ThrowsAsync<DomainException>(() => _auth.SaveRoleAsync(new RoleDto(1, "Autre")));
        await Assert.ThrowsAsync<DomainException>(() => _auth.DeleteRoleAsync(1));
        await Assert.ThrowsAsync<DomainException>(() => _auth.SetPrivilegesAsync(1, new List<decimal> { Rights.Reports }));
        Assert.Equal("Administrateur", _uow.GetRepo<Role>().Items.First(r => r.IdRole == 1).NomRole);
    }

    [Fact]
    public async Task OtherRoles_RemainEditable()
    {
        await _auth.SetPrivilegesAsync(5, new List<decimal> { Rights.Reports });
        Assert.Single(_uow.GetRepo<DroitRole>().Items.Where(d => d.IdRole == 5));
        await _auth.SaveRoleAsync(new RoleDto(5, "Gestionnaire paie"));
        Assert.Equal("Gestionnaire paie", _uow.GetRepo<Role>().Items.First(r => r.IdRole == 5).NomRole);
        await _auth.DeleteRoleAsync(5);
        Assert.DoesNotContain(_uow.GetRepo<Role>().Items, r => r.IdRole == 5);
    }

    #endregion

    #region Doubles de test

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(decimal userId, decimal roleId, params int[] rights)
        {
            UserId = userId;
            RoleId = roleId;
            Rights = rights;
        }
        public decimal? UserId { get; }
        public string? Login => $"u{UserId}";
        public decimal? RoleId { get; }
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights { get; }
        public bool HasRight(int droit) => RoleId == 1 || Rights.Contains(droit);
    }

    private sealed class InMemoryRepository<T> : IRepository<T> where T : class
    {
        public List<T> Items { get; } = new();

        public IQueryable<T> Query() => Items.AsQueryable();

        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        {
            var decId = Convert.ToDecimal(id);
            var match = Items.FirstOrDefault(i => i switch
            {
                Responsable r => r.IdResponsable == decId,
                Role ro => ro.IdRole == decId,
                BddSage s => s.Id == decId,
                BddPointeuse p => p.Id == decId,
                _ => false
            });
            return Task.FromResult(match);
        }

        public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Where(predicate.Compile()).ToList());

        public Task<List<T>> ListLatestAsync(
            Expression<Func<T, bool>> predicate,
            Expression<Func<T, DateTime?>> orderByDescending,
            int take,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Where(predicate.Compile()).OrderByDescending(orderByDescending.Compile()).Take(take).ToList());

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
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
            foreach (var e in entities.ToList())
                Items.Remove(e);
        }
    }

    private sealed class InMemoryUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repos = new();

        public InMemoryRepository<T> GetRepo<T>() where T : class
        {
            if (!_repos.TryGetValue(typeof(T), out var r))
            {
                r = new InMemoryRepository<T>();
                _repos[typeof(T)] = r;
            }
            return (InMemoryRepository<T>)r;
        }

        public IRepository<T> Repository<T>() where T : class => GetRepo<T>();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTokenService : ITokenService
    {
        public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights)
            => $"FAKE_JWT_TOKEN_{userId}_{login}";
    }

    private sealed class FakeEmailService : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    #endregion
}
