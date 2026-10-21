using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoftTime.Infrastructure;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Tests;

/// <summary>
/// Régression du correctif de compatibilité SQL Server : la base cible est en niveau 100 et
/// EF Core 8 traduit par défaut les <c>Contains</c> de collection en <c>OPENJSON</c>, rejeté par
/// SQL Server. La configuration d'infrastructure doit forcer des listes <c>IN</c>.
/// </summary>
public class SqlCompatibilityTests
{
    [Fact]
    public void CollectionContains_WithInfrastructureOptions_UsesInAndNotOpenJson()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SoftTime"] =
                    "Server=(local);Database=SoftTime;Trusted_Connection=True;TrustServerCertificate=True"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DbContextOptions<SoftTimeDbContext>>();

        // Vérifie la configuration réellement enregistrée par AddInfrastructure : sans le niveau 120,
        // EF Core 8 génère OPENJSON pour les Contains de collection, rejeté par la base (niveau 100).
        var extension = options.Extensions
            .FirstOrDefault(e => e.GetType().Name == "SqlServerOptionsExtension");
        Assert.NotNull(extension);
        var compatibility = extension!.GetType().GetProperty("CompatibilityLevel");
        Assert.NotNull(compatibility);
        Assert.Equal(120, compatibility!.GetValue(extension));
    }
}
