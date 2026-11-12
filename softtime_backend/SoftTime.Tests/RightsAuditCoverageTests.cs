using AutoMapper;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;

using SoftTime.Api.Middleware;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>
/// Audit (section 7) : chaque modification de droits, de rôle comme de compte, écrit acteur, cible, avant, après et date ;
/// chaque accès refusé (403) est journalisé sans donnée sensible.
/// </summary>
public class RightsAuditCoverageTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly AuthServices _auth;

    public RightsAuditCoverageTests()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        _auth = new AuthServices(_uow, ServiceMocks.TokenService().Object, mapper, ServiceMocks.EmailService().Object,
            new AdminCurrentUser(), TestRights.Service(_uow), new PasswordHasher<Responsable>());
        _uow.Get<Role>().Items.Add(new Role { IdRole = 1, NomRole = "Administrateur" });
        _uow.Get<Role>().Items.Add(new Role { IdRole = 3, NomRole = "Gestionnaire" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 1, IdRole = 1, Login = "admin", NomResponsable = "Admin" });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 3, IdRole = 3, Login = "user3", NomResponsable = "User 3" });
        _uow.Get<BddSage>().Items.Add(new BddSage { Id = 7, NomBd = "PAIE" });
    }

    private AuditDroit Single(string action)
        => _uow.Get<AuditDroit>().Items.Should().ContainSingle(a => a.Action == action).Which;

    private static void ShouldNameActorAndDate(AuditDroit entry)
    {
        entry.IdOperateur.Should().Be(1);
        entry.LoginOperateur.Should().NotBeNullOrWhiteSpace();
        entry.DateAction.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SetPrivilegesAsync_RoleGates_AreJournaled()
    {
        // Arrange
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 3, IdDroit = 3 });

        // Act
        await _auth.Privileges.SetPrivilegesAsync(3, new decimal[] { 2, 3 });

        // Assert
        var entry = Single("UPDATE_ROLE_GATES");
        ShouldNameActorAndDate(entry);
        (entry.TypeCible, entry.IdCible, entry.NomCible).Should().Be(("ROLE", 3m, "Gestionnaire"));
        entry.PermsAvant.Should().Be("PORTE:3");
        entry.PermsApres.Should().Be("PORTE:2,PORTE:3");
    }

    [Fact]
    public async Task SetDbAccessAsync_CheckedDatabases_AreJournaledByName()
    {
        // Act
        await _auth.DatabaseAccess.SetDbAccessAsync(3, new[] { 7 });

        // Assert
        var entry = Single("UPDATE_USER_DB_ACCESS");
        ShouldNameActorAndDate(entry);
        (entry.TypeCible, entry.IdCible).Should().Be(("USER", 3m));
        entry.PermsAvant.Should().BeEmpty();
        entry.PermsApres.Should().Be("BASE:PAIE");
    }

    [Fact]
    public async Task DeleteRoleAsync_RemovedRights_AreJournaled()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 4, NomRole = "Jetable" });
        _uow.Get<RolePermission>().Items.Add(new RolePermission { IdRole = 4, PermissionCode = "PROC:SHIFTS", IsGranted = true });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 4, IdDroit = 3 });

        // Act
        await _auth.Roles.DeleteRoleAsync(4);

        // Assert
        var entry = Single("DELETE_ROLE");
        ShouldNameActorAndDate(entry);
        entry.PermsAvant.Should().Be("PROC:SHIFTS,PORTE:3");
        entry.PermsApres.Should().BeNull();
    }

    [Fact]
    public async Task SaveRoleAndUserPermissions_BothAreJournaledWithBeforeAndAfter()
    {
        // Act
        await _auth.SaveRoleDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS" });
        await _auth.SaveUserDetailedPermissionsAsync(1, 3, new[] { "PROC:SHIFTS", "PARAMS:USERS" });

        // Assert
        var role = Single("UPDATE_ROLE_PERMS");
        ShouldNameActorAndDate(role);
        role.PermsApres.Should().Be("PROC:SHIFTS");
        var user = Single("UPDATE_USER_PERMS");
        ShouldNameActorAndDate(user);
        user.PermsAvant.Should().Be("PROC:SHIFTS");
        user.PermsApres.Should().Be("PROC:SHIFTS,PARAMS:USERS");
    }

    [Fact]
    public async Task ForbiddenAccessLogging_403_LogsAccountMethodAndRouteTemplateOnly()
    {
        // Arrange
        var logger = new ListLogger();
        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("uid", "42") }, "Test"));
        context.Request.Method = "DELETE";
        context.Request.Path = "/api/v1/departement-service/lookup/MAT-SECRET";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/v1/departement-service/lookup/{matricule}"), 0, EndpointMetadataCollection.Empty, "lookup"));
        var sut = new ForbiddenAccessLoggingMiddleware(ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; }, logger);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        var line = logger.Lines.Should().ContainSingle().Which;
        line.Should().Contain("42").And.Contain("DELETE").And.Contain("api/v1/departement-service/lookup/{matricule}");
        line.Should().NotContain("MAT-SECRET");
    }

    [Fact]
    public async Task ForbiddenAccessLogging_SuccessfulRequest_LogsNothing()
    {
        // Arrange
        var logger = new ListLogger();
        var sut = new ForbiddenAccessLoggingMiddleware(ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; }, logger);

        // Act
        await sut.InvokeAsync(new DefaultHttpContext());

        // Assert
        logger.Lines.Should().BeEmpty();
    }

    private sealed class ListLogger : ILogger<ForbiddenAccessLoggingMiddleware>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception));
    }
}
