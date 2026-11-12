using Microsoft.Extensions.Logging.Abstractions;

using SoftTime.Application.Security;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services.Permissions;

namespace SoftTime.Tests.Http;

/// <summary>Service de droits effectifs pour les tests unitaires : cache vide propre à chaque appel, journal muet.</summary>
public static class TestRights
{
    public static EffectiveRightsService Service(IUnitOfWork uow, EffectiveRightsCache? cache = null)
        => new(uow, cache ?? new EffectiveRightsCache(), NullLogger<EffectiveRightsService>.Instance);

    /// <summary>
    /// Migration 006 : convertit en écarts les copies complètes amorcées par un test (IsDeviation = false), comme le fait
    /// le démarrage de l'API. Les droits effectifs ne changent pas (UserPermissionDeviationMigrationTests).
    /// </summary>
    public static Task<int> ConvertLegacyCopiesAsync(IUnitOfWork uow)
        => new UserPermissionDeviationMigration(uow, Service(uow), NullLogger<UserPermissionDeviationMigration>.Instance).RunAsync();
}
