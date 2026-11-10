using Microsoft.Extensions.DependencyInjection;

using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Domain.Services;

namespace SoftTime.Tests.Http;

/// <summary>
/// Traitements d'arrière-plan : le scope d'arrière-plan n'a pas d'utilisateur HTTP. Activé
/// explicitement, il s'exécute sous l'identité système ; sinon il reste anonyme et refusé.
/// </summary>
public class SystemExecutionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;

    public SystemExecutionTests(ApiFactory factory) => _f = factory;

    [Fact]
    public async Task BackgroundScope_SystemExecution_RunsAsAdministratorWithSageAccess()
    {
        // Arrange
        using var scope = _f.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SystemExecution>().Enable();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(ApiFactory.SageName, null);

        // Act
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        // Assert
        user.RoleId.Should().Be(AdminRules.AdminRoleId);
        user.Login.Should().Be(SystemExecution.Login);
        await scope.ServiceProvider.GetRequiredService<ITenantConnectionService>().EnsureAuthorizedAsync();
    }

    [Fact]
    public async Task BackgroundScope_WithoutSystemExecution_StaysAnonymousAndIsDenied()
    {
        // Arrange
        using var scope = _f.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(ApiFactory.SageName, null);

        // Act
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        // Assert
        user.UserId.Should().BeNull();
        user.RoleId.Should().BeNull();
        await FluentActions.Awaiting(() => scope.ServiceProvider.GetRequiredService<ITenantConnectionService>().EnsureAuthorizedAsync()).Should().ThrowExactlyAsync<ForbiddenException>();
    }
}
