using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using AutoMapper;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using SoftTime.Api.Authorization;
using SoftTime.Api.Middleware;
using SoftTime.Api.Security;
using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.DataSources;
using SoftTime.Infrastructure.Services.Permissions;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>
/// Couverture complémentaire des privilèges (trous de la matrice) : filtres d'autorisation pris isolément, listes de bases,
/// lectures fines (FineReadAccess, ParametersAccessGuard), contenu du jeton, permissions renvoyées à l'administrateur et
/// autorisation de configuration des bases. Aucune règle n'est modifiée : chaque test décrit le comportement actuel.
/// </summary>
public class PrivilegeGuardsCoverageTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly AuthServices _auth;

    public PrivilegeGuardsCoverageTests()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthServices(_uow, ServiceMocks.TokenService().Object, mapper, ServiceMocks.EmailService().Object, new AdminCurrentUser(), TestRights.Service(_uow), new PasswordHasher<Responsable>());
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 5, NomRole = "Gestionnaire" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "a1" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = "BASE1", Serveur = "srv" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 9, NomBd = "Base_Mixte", Serveur = "srv" });
        _uow.Get<BddPointeuse>().Items.Add(new BddPointeuse { Id = 8, NomBd = "PTG", Serveur = "srv" });
    }

    private sealed class Anonymous : ICurrentUser
    {
        public decimal? UserId => null;
        public string? Login => null;
        public decimal? RoleId => null;
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights => Array.Empty<int>();
        public bool HasRight(int droit) => false;
    }

    private void Account(decimal id, decimal role, params string[] userRows)
    {
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = id, IdRole = role, Login = $"u{id}" });
        foreach (var c in userRows)
            _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = id, PermissionCode = c, IsGranted = true });
    }

    private static AuthorizationFilterContext FilterContext(bool authenticated)
    {
        var identity = authenticated ? new ClaimsIdentity(new[] { new Claim("uid", "1") }, "Test") : new ClaimsIdentity();
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
    }

    // ------------------------------------------------------------------ filtres d'autorisation

    public static TheoryData<bool, decimal, int[], bool> RequireDroitCases => new()
    {
        // authentifié, rôle, portes, refusé
        { false, 5, Array.Empty<int>(), false },          // non authentifié : le filtre laisse [Authorize] répondre 401
        { true, 5, Array.Empty<int>(), true },            // authentifié sans porte : 403
        { true, 5, new[] { Rights.Reports }, true },      // autre porte : 403
        { true, 5, new[] { Rights.Databases }, false },   // porte demandée : passe
        { true, 1, Array.Empty<int>(), false },           // administrateur : passe
    };

    [Theory]
    [MemberData(nameof(RequireDroitCases))]
    public void RequireDroitFilter_Matrix(bool authenticated, decimal role, int[] rights, bool refused)
    {
        // Arrange
        var ctx = FilterContext(authenticated);

        // Act
        new RequireDroitFilter(Rights.Databases, new TestCurrentUser(2, role, rights)).OnAuthorization(ctx);

        // Assert
        if (refused) ctx.Result.Should().BeOfType<ForbidResult>();
        else ctx.Result.Should().BeNull();
    }

    public static TheoryData<bool, decimal, int[], bool> RequireAnyDroitCases => new()
    {
        { false, 5, Array.Empty<int>(), false },
        { true, 5, Array.Empty<int>(), true },
        { true, 5, new[] { Rights.Reports }, true },
        { true, 5, new[] { Rights.Databases }, false },
        { true, 5, new[] { Rights.Parameters }, false },
        { true, 5, new[] { Rights.Databases, Rights.Parameters }, false },
        { true, 1, Array.Empty<int>(), false },
    };

    [Theory]
    [MemberData(nameof(RequireAnyDroitCases))]
    public void RequireAnyDroitFilter_Matrix(bool authenticated, decimal role, int[] rights, bool refused)
    {
        var ctx = FilterContext(authenticated);

        new RequireAnyDroitFilter(new[] { Rights.Databases, Rights.Parameters }, new TestCurrentUser(2, role, rights)).OnAuthorization(ctx);

        if (refused) ctx.Result.Should().BeOfType<ForbidResult>();
        else ctx.Result.Should().BeNull();
    }

    [Fact]
    public void RequireAnyDroitFilter_EmptyList_RefusesEvenTheAdministrator()
    {
        // Comportement actuel : aucune porte demandée => « aucune des portes » est vrai => refus, même pour l'administrateur.
        var ctx = FilterContext(true);

        new RequireAnyDroitFilter(Array.Empty<int>(), new TestCurrentUser(1, 1)).OnAuthorization(ctx);

        ctx.Result.Should().BeOfType<ForbidResult>();
    }

    [Theory]
    [InlineData(false, 5, false)]
    [InlineData(true, 5, true)]
    [InlineData(true, 4, true)]
    [InlineData(true, 1, false)]
    public void RequireAdminFilter_Matrix(bool authenticated, int role, bool refused)
    {
        var ctx = FilterContext(authenticated);

        new RequireAdminFilter(new TestCurrentUser(2, role, Rights.Databases, Rights.Parameters)).OnAuthorization(ctx);

        if (refused)
        {
            var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
            result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }
        else
        {
            ctx.Result.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(5, new int[0], true)]
    [InlineData(5, new[] { Rights.Databases }, false)]
    [InlineData(1, new int[0], false)]
    public async Task DroitFilter_EndpointFilter_Matrix(int role, int[] rights, bool refused)
    {
        // DroitFilter n'est branché sur aucune route dans le code actuel (filtre de point de terminaison minimal) ; testé tel quel.
        var services = new ServiceCollection().AddSingleton<ICurrentUser>(new TestCurrentUser(2, role, rights)).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var invocation = new DefaultEndpointFilterInvocationContext(http);

        var result = await new DroitFilter(Rights.Databases).InvokeAsync(invocation, _ => ValueTask.FromResult<object?>("suite"));

        if (refused) result.Should().BeOfType<ForbidHttpResult>();
        else result.Should().Be("suite");
    }

    // ------------------------------------------------------------------ listes de bases

    [Theory]
    [InlineData(5, new int[0], false)]
    [InlineData(5, new[] { Rights.Reports }, false)]
    [InlineData(5, new[] { Rights.Databases }, true)]
    [InlineData(5, new[] { Rights.Parameters }, true)]
    [InlineData(1, new int[0], true)]
    public void DatabaseListAccess_PeutLire_Gate1Or2OrAdministrator(int role, int[] rights, bool canRead)
        => DatabaseListAccess.PeutLire(new TestCurrentUser(2, role, rights)).Should().Be(canRead);

    [Theory]
    [InlineData(null, null, 50)]
    [InlineData(3, 10, 10)]
    [InlineData(1, 100000, 100)]
    public void DatabaseListAccess_PageVide_IsEmptyFirstPageWithNormalizedSize(int? page, int? size, int expectedSize)
    {
        var empty = DatabaseListAccess.PageVide<string>(page, size);

        empty.Items.Should().BeEmpty();
        empty.Total.Should().Be(0);
        empty.Page.Should().Be(1);
        empty.PageSize.Should().Be(expectedSize);
    }

    // ------------------------------------------------------------------ lectures fines (porte + permission, lues côté serveur)

    private void RoleWithRows(decimal role, params string[] codes)
    {
        foreach (var c in codes)
            _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = role, PermissionCode = c, IsGranted = true });
    }

    [Fact]
    public async Task FineReadAccess_Matrix()
    {
        // Arrange : rôle 5 avec « Rapports » (porte 6) et un droit de Paramètres (porte 2).
        RoleWithRows(5, "REPORTS:REPORTS", "PARAMS:USERS");
        Account(30, 5);
        var rights = TestRights.Service(_uow);

        // Act & Assert
        await FineReadAccess.EnsureAsync(new TestCurrentUser(1, 1), rights, Rights.Reports, "REPORTS:CLOSING");                        // administrateur
        await FineReadAccess.EnsureAsync(new TestCurrentUser(30, 5), rights, Rights.Reports, "REPORTS:REPORTS");                       // porte + permission
        await FluentActions.Awaiting(() => FineReadAccess.EnsureAsync(new TestCurrentUser(30, 5), rights, Rights.Reports, "REPORTS:CLOSING"))
            .Should().ThrowExactlyAsync<ForbiddenException>();                                                                        // porte sans la permission
        await FluentActions.Awaiting(() => FineReadAccess.EnsureAsync(new TestCurrentUser(30, 5), rights, Rights.Processing, "REPORTS:REPORTS"))
            .Should().ThrowExactlyAsync<ForbiddenException>();                                                                        // permission sans la porte demandée
        await FluentActions.Awaiting(() => FineReadAccess.EnsureAsync(new Anonymous(), rights, Rights.Reports, "REPORTS:REPORTS"))
            .Should().ThrowExactlyAsync<ForbiddenException>();                                                                        // compte non identifié
    }

    [Fact]
    public async Task FineReadAccess_RightsUnreadable_FailsClosedAndLogs()
    {
        var rights = new Mock<IEffectiveRightsService>();
        rights.Setup(r => r.GetAsync(30, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("base indisponible"));

        await FluentActions.Awaiting(() => FineReadAccess.EnsureAsync(new TestCurrentUser(30, 5, Rights.Reports), rights.Object, Rights.Reports, "REPORTS:REPORTS"))
            .Should().ThrowExactlyAsync<ForbiddenException>();
        rights.Verify(r => r.LogReadFailure(30, It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public async Task ParametersAccessGuard_Matrix()
    {
        // Arrange
        RoleWithRows(5, "PARAMS:USERS", "REPORTS:REPORTS");
        Account(31, 5);
        _uow.Get<Role>().Items.Add(new Role { IdRole = 6, NomRole = "Rapports seuls" });
        RoleWithRows(6, "REPORTS:REPORTS");
        Account(32, 6);
        var rights = TestRights.Service(_uow);
        ParametersAccessGuard Guard(ICurrentUser u) => new(u, rights);

        // Act & Assert
        await Guard(new TestCurrentUser(1, 1)).EnsureCanReadAsync(new[] { "PARAMS:ROLES" });                                         // administrateur
        await Guard(new TestCurrentUser(31, 5)).EnsureCanReadAsync(new[] { "PARAMS:ROLES", "PARAMS:USERS" });                         // porte 2 + l'une des permissions
        await FluentActions.Awaiting(() => Guard(new TestCurrentUser(31, 5)).EnsureCanReadAsync(new[] { "PARAMS:ROLES" }))
            .Should().ThrowExactlyAsync<ForbiddenException>();                                                                        // porte 2 sans la permission
        await FluentActions.Awaiting(() => Guard(new TestCurrentUser(32, 6)).EnsureCanReadAsync(new[] { "REPORTS:REPORTS" }))
            .Should().ThrowExactlyAsync<ForbiddenException>();                                                                        // permission sans la porte 2
        await FluentActions.Awaiting(() => Guard(new Anonymous()).EnsureCanReadAsync(new[] { "PARAMS:USERS" }))
            .Should().ThrowExactlyAsync<ForbiddenException>();
        FluentActions.Invoking(() => Guard(new TestCurrentUser(31, 5, Rights.Parameters)).RequireAdministrator())
            .Should().ThrowExactly<ForbiddenException>();
        Guard(new TestCurrentUser(1, 1)).RequireAdministrator();
    }

    [Fact]
    public async Task ParametersAccessGuard_RightsUnreadable_FailsClosedAndLogs()
    {
        var rights = new Mock<IEffectiveRightsService>();
        rights.Setup(r => r.GetAsync(31, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("x"));

        await FluentActions.Awaiting(() => new ParametersAccessGuard(new TestCurrentUser(31, 5, Rights.Parameters), rights.Object).EnsureCanReadAsync(new[] { "PARAMS:USERS" }))
            .Should().ThrowExactlyAsync<ForbiddenException>();
        rights.Verify(r => r.LogReadFailure(31, It.IsAny<Exception>()), Times.Once);
    }

    // ------------------------------------------------------------------ jeton d'accès

    [Fact]
    public void JwtTokenService_RightsClaim_ListsTheGatesAndCarriesNoDatabase()
    {
        // Arrange
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Key = "test-only-signing-key-0123456789-abcdefghijklmnop",
            Issuer = "SoftTime.Tests",
            Audience = "SoftTime.Tests.Web",
        }));

        // Act
        var avecPortes = new JwtSecurityTokenHandler().ReadJwtToken(service.CreateToken(new TokenSubject(5, "x", 5, "M1", new List<int> { 1, 3 })));
        var sansPorte = new JwtSecurityTokenHandler().ReadJwtToken(service.CreateToken(new TokenSubject(6, "y", 92, null, new List<int>())));

        // Assert
        avecPortes.Claims.Single(c => c.Type == "rights").Value.Should().Be("1,3");
        avecPortes.Claims.Single(c => c.Type == "roleId").Value.Should().Be("5");
        sansPorte.Claims.Single(c => c.Type == "rights").Value.Should().BeEmpty();
        sansPorte.Claims.Single(c => c.Type == "matricule").Value.Should().BeEmpty();
        avecPortes.Claims.Select(c => c.Type).Should().NotContain(t =>
            t.Contains("sage", StringComparison.OrdinalIgnoreCase) || t.Contains("base", StringComparison.OrdinalIgnoreCase)
            || t.Contains("permission", StringComparison.OrdinalIgnoreCase));                               // ni base ni permission fine dans le jeton
    }

    // ------------------------------------------------------------------ permissions renvoyées (session du frontend)

    [Fact]
    public async Task GetUserDetailedPermissionsAsync_CustomizedAdministrator_GetsOwnRowsPlusEveryExistingDatabase()
    {
        // Arrange : l'administrateur n'a de lignes propres que sur BASE1.
        _uow.Get<UserPermission>().Items.Add(new UserPermission { IdResponsable = 1, PermissionCode = "SAGE:BASE1:READ", IsGranted = true });

        // Act
        var p = await _auth.UserPermissions.GetUserDetailedPermissionsAsync(1);

        // Assert
        p.IsCustomized.Should().BeTrue();
        p.GrantedPermissions.Should().Contain(new[] { "SAGE:CREATE", "SAGE:BASE1:UPDATE", "SAGE:Base_Mixte:UPDATE", "SAGE:Base_Mixte:DELETE", "POINTEUSE:PTG:UPDATE" });
        p.GrantedPermissions.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetUserDetailedPermissionsAsync_AdministratorWithoutRows_IsNeverEmpty()
    {
        var p = await _auth.UserPermissions.GetUserDetailedPermissionsAsync(1);

        p.IsCustomized.Should().BeFalse();
        p.GrantedPermissions.Should().NotBeEmpty().And.Contain("SAGE:BASE1:UPDATE");
    }

    [Fact]
    public async Task GetUserDetailedPermissionsAsync_DeletedDatabase_DisappearsFromTheAdministratorDefaults()
    {
        _uow.Get<BddSage>().Items.RemoveAll(s => s.Id == 9);

        var p = await _auth.UserPermissions.GetUserDetailedPermissionsAsync(1);

        p.GrantedPermissions.Should().NotContain(c => c.StartsWith("SAGE:Base_Mixte:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetUserDetailedPermissionsAsync_DatabaseCodes_KeepTheStoredCase()
    {
        // Comportement actuel : le nom de base n'est pas normalisé dans les codes (« Base_Mixte », pas « BASE_MIXTE »).
        var p = await _auth.UserPermissions.GetUserDetailedPermissionsAsync(1);

        p.GrantedPermissions.Should().Contain("SAGE:Base_Mixte:UPDATE").And.NotContain("SAGE:BASE_MIXTE:UPDATE");
    }

    [Fact]
    public async Task GetUserDetailedPermissionsAsync_CustomizedNonAdministrator_GetsOnlyItsOwnRows()
    {
        Account(40, 5, "SAGE:BASE1:READ", "SAGE:BASE1:UPDATE");

        var p = await _auth.UserPermissions.GetUserDetailedPermissionsAsync(40);

        p.IsCustomized.Should().BeTrue();
        p.GrantedPermissions.Should().BeEquivalentTo(new[] { "SAGE:BASE1:READ", "SAGE:BASE1:UPDATE" });
    }

    // ------------------------------------------------------------------ configuration des bases (serveur)

    private DatabaseConfigurationAuthorizer Authorizer(decimal id, decimal role, params int[] rights)
        => new(new TestCurrentUser(id, role, rights), _auth.UserPermissions);

    [Fact]
    public async Task DatabaseConfigurationAuthorizer_PermissionCase_IsIgnored()
    {
        // Le serveur compare les codes sans tenir compte de la casse (le frontend, lui, compare tel quel : voir le rapport).
        Account(41, 5, "sage:base1:update");

        await Authorizer(41, 5, Rights.Databases).EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1");
        await Authorizer(41, 5, Rights.Databases).EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, "base1");
    }

    [Fact]
    public async Task DatabaseConfigurationAuthorizer_ParametersGateAlone_IsRefusedEvenWithThePermission()
    {
        Account(42, 5, "SAGE:CREATE", "SAGE:BASE1:UPDATE");

        await FluentActions.Awaiting(() => Authorizer(42, 5, Rights.Parameters).EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, "BASE1"))
            .Should().ThrowExactlyAsync<ForbiddenException>();
    }

    [Fact]
    public async Task DatabaseConfigurationAuthorizer_PerDatabaseCreateCode_AllowsCreation()
    {
        Account(43, 5, "SAGE:AUTRE:CREATE");

        await Authorizer(43, 5, Rights.Databases).EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, "NOUVELLE");
        await FluentActions.Awaiting(() => Authorizer(43, 5, Rights.Databases).EnsureCanConfigureAsync(DatabaseKind.Pointeuse, DatabaseConfigAction.Create, "P"))
            .Should().ThrowExactlyAsync<ForbiddenException>();
    }

    [Fact]
    public async Task DatabaseConfigurationAuthorizer_Administrator_PassesEvenForABlankStoredName()
    {
        // Comportement actuel : l'administrateur sort avant le contrôle du nom ; un compte ordinaire est refusé (test existant).
        await Authorizer(1, 1).EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Delete, "");
    }

    [Fact]
    public async Task DatabaseConfigurationAuthorizer_UnknownAccount_IsRefused()
    {
        await FluentActions.Awaiting(() => Authorizer(999, 5, Rights.Databases).EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Create, "X"))
            .Should().ThrowExactlyAsync<ForbiddenException>();
    }
}
