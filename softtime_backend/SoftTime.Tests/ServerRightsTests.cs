using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using SoftTime.Api.Middleware;
using SoftTime.Api.Security;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>Lot 5 : rôle et portes du compte lus en base pour chaque requête, à la place du jeton.</summary>
public class ServerRightsTests
{
    private readonly InMemoryUnitOfWork _uow = new();

    private static DefaultHttpContext Authenticated() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", "5") }, "Test")),
    };

    [Fact]
    public void CurrentUser_WithServerRights_IgnoresTokenRoleAndGates()
    {
        // Arrange
        var sut = new CurrentUser { UserId = 5, TokenRoleId = 1, TokenRights = new[] { 1, 2 }, Server = () => new ServerRights(7, new[] { 3 }) };

        // Act
        var hasGate2 = sut.HasRight(2);

        // Assert
        hasGate2.Should().BeFalse();
        sut.HasRight(3).Should().BeTrue();
        sut.RoleId.Should().Be(7);
        sut.Rights.Should().Equal(3);
    }

    [Fact]
    public void CurrentUser_WithoutServerRights_UsesTokenValues()
    {
        // Arrange
        // Identité système des tâches de fond : aucun middleware HTTP.
        var sut = new CurrentUser { TokenRoleId = 1 };

        // Act
        var isAdmin = sut.RoleId;

        // Assert
        isAdmin.Should().Be(1);
        sut.HasRight(4).Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_AuthenticatedAccount_PostsDatabaseRoleAndGates()
    {
        // Arrange
        _uow.Get<Role>().Items.Add(new Role { IdRole = 7, NomRole = "Traitements" });
        _uow.Get<DroitRole>().Items.Add(new DroitRole { IdRole = 7, IdDroit = 3 });
        _uow.Get<Responsable>().Items.Add(new Responsable { IdResponsable = 5, IdRole = 7, Login = "u5" });
        var context = Authenticated();
        var sut = new ServerRightsMiddleware(_ => Task.CompletedTask);

        // Act
        await sut.InvokeAsync(context, new TestCurrentUser(5, 1, 1, 2, 3, 4, 5, 6), TestRights.Service(_uow));

        // Assert
        var rights = (ServerRights)context.Items[ServerRights.ItemKey]!;
        rights.RoleId.Should().Be(7);
        rights.Gates.Should().Equal(3);
    }

    [Fact]
    public async Task Middleware_ReadFailure_FailsClosed()
    {
        // Arrange
        var context = Authenticated();
        var rights = new FailingRights();
        var sut = new ServerRightsMiddleware(_ => Task.CompletedTask);

        // Act
        await sut.InvokeAsync(context, new TestCurrentUser(5, 1, 1, 2, 3, 4, 5, 6), rights);

        // Assert
        context.Items[ServerRights.ItemKey].Should().BeSameAs(ServerRights.None);
        rights.Logged.Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_AnonymousRequest_PostsNothing()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var sut = new ServerRightsMiddleware(_ => Task.CompletedTask);

        // Act
        await sut.InvokeAsync(context, new TestCurrentUser(5, 1), new FailingRights());

        // Assert
        context.Items.Should().NotContainKey(ServerRights.ItemKey);
    }

    private sealed class FailingRights : IEffectiveRightsService
    {
        public bool Logged { get; private set; }

        public void Invalidate(decimal userId) { }

        public void InvalidateAll() { }

        public Task<EffectiveRights> GetAsync(decimal userId, bool fresh = false, CancellationToken ct = default)
            => throw new InvalidOperationException("base indisponible");

        public Task<IReadOnlyList<string>> AdjustmentsAsync(decimal userId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> RoleDefaultPermissionsAsync(decimal roleId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public void LogReadFailure(decimal userId, Exception ex) => Logged = true;
    }
}
