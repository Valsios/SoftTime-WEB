using Microsoft.Extensions.DependencyInjection;
using SoftTime.Application.Abstractions;

namespace SoftTime.Application.Services;

/// <summary>
/// Applique les migrations EF Core du contexte <c>MlDbContext</c> au démarrage. Les migrations
/// sont versionnées dans le dépôt et le script idempotent est généré par
/// <c>dotnet ef migrations script --idempotent</c> pour les déploiements.
/// </summary>
public static class MlStorageConfiguration
{
    public static async Task EnsureSchemaAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IMlStore>();
        await store.EnsureSchemaAsync(ct);
    }
}
