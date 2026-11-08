using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
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
        _auth = new AuthServices(_uow, ServiceMocks.TokenService().Object, _mapper, ServiceMocks.EmailService().Object, new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Gestionnaire" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = Base, Serveur = "srv" });
        _uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 8, NomBd = "PTG", Serveur = "srv" });
    }

    private DatabaseConfigurationAuthorizer AuthorizerFor(decimal userId, decimal roleId, params int[] rights)
    {
        if (!_uow.Get<Responsable>().Items.Any(u => u.IdResponsable == userId))
            _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = userId, IdRole = roleId, Login = $"u{userId}" });
        return new DatabaseConfigurationAuthorizer(new TestCurrentUser(userId, roleId, rights), _auth);
    }

    private void GrantUser(decimal userId, params string[] codes)
    {
        foreach (var c in codes)
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = userId, PermissionCode = c, IsGranted = true });
    }

    private void GrantRole(decimal roleId, params string[] codes)
    {
        foreach (var c in codes)
            _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = roleId, PermissionCode = c, IsGranted = true });
    }

    private static async Task<ForbiddenException> Refused(Func<Task> act) => (await act.Should().ThrowExactlyAsync<ForbiddenException>()).Which;

    #region AdminAccess

    [Fact]
    public void AdminAccess_OnlyRoleOneIsAdmin()
    {
        AdminAccess.IsAdmin(new TestCurrentUser(1, 1)).Should().BeTrue();
        AdminAccess.IsAdmin(new TestCurrentUser(2, 5, Rights.Parameters)).Should().BeFalse();
        FluentActions.Invoking(() => AdminAccess.Require(new TestCurrentUser(2, 5, Rights.Parameters))).Should().ThrowExactly<ForbiddenException>();
        AdminAccess.Require(new TestCurrentUser(1, 1));
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
        var a = new DatabaseConfigurationAuthorizer(new TestCurrentUser(999, 5, Rights.Databases), _auth);
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
        _uow.Get<BddSage>().Items.Should().ContainSingle();
    }

    [Fact]
    public async Task CatalogService_DeleteSage_EmptyName_IsRefused()
    {
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 9, NomBd = "", Serveur = "srv" });
        GrantUser(31, "SAGE:CREATE", "SAGE::DELETE");
        var svc = CatalogFor(AuthorizerFor(31, 5, Rights.Databases));
        await Refused(() => svc.Sage.DeleteSageAsync(9));
        _uow.Get<BddSage>().Items.Count.Should().Be(2);
    }

    [Fact]
    public async Task CatalogService_SaveSage_CreateAndUpdate_RefusedWithoutPermission()
    {
        var svc = CatalogFor(AuthorizerFor(32, 5, Rights.Reports));
        var dto = new SageDbDto(0, "srv", null, null, true, "NOUVELLE");
        await Refused(() => svc.Sage.SaveSageAsync(dto));
        await Refused(() => svc.Sage.SaveSageAsync(dto with { Id = 7 }));
        _uow.Get<BddSage>().Items.Should().ContainSingle();
    }

    [Fact]
    public async Task CatalogService_Pointeuse_SaveAndDelete_RefusedWithoutPermission()
    {
        var svc = CatalogFor(AuthorizerFor(33, 5, Rights.Reports));
        await Refused(() => svc.Pointeuse.DeletePointeuseAsync(8));
        await Refused(() => svc.Pointeuse.SavePointeuseAsync(new PointeuseDbDto(0, "srv", null, null, true, "NEW", null, false)));
        _uow.Get<BddPointeuse>().Items.Should().ContainSingle();
    }

    #endregion

    #region Rôle Administrateur intact

    [Fact]
    public async Task AdminRole_CannotBeRenamedDeletedOrHaveItsPrivilegesReplaced()
    {
        await FluentActions.Awaiting(() => _auth.SaveRoleAsync(new RoleDto(1, "Autre"))).Should().ThrowExactlyAsync<DomainException>();
        await FluentActions.Awaiting(() => _auth.DeleteRoleAsync(1)).Should().ThrowExactlyAsync<DomainException>();
        await FluentActions.Awaiting(() => _auth.SetPrivilegesAsync(1, new List<decimal> { Rights.Reports })).Should().ThrowExactlyAsync<DomainException>();
        _uow.Get<Role>().Items.First(r => r.IdRole == 1).NomRole.Should().Be("Administrateur");
    }

    [Fact]
    public async Task OtherRoles_RemainEditable()
    {
        await _auth.SetPrivilegesAsync(5, new List<decimal> { Rights.Reports });
        _uow.Get<DroitRole>().Items.Where(d => d.IdRole == 5).Should().ContainSingle();
        await _auth.SaveRoleAsync(new RoleDto(5, "Gestionnaire paie"));
        _uow.Get<Role>().Items.First(r => r.IdRole == 5).NomRole.Should().Be("Gestionnaire paie");
        await _auth.DeleteRoleAsync(5);
        _uow.Get<Role>().Items.Should().NotContain(r => r.IdRole == 5);
    }

    #endregion

    #region Doubles de test

    #endregion
}
