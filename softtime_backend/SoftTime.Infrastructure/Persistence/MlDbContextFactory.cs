using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SoftTime.Infrastructure.Persistence;

/// <summary>
/// Fabrique de design-time utilisée par <c>dotnet ef</c>. Elle lit la chaîne
/// <c>SoftTimeMl</c> (repli <c>SoftTime</c>) depuis appsettings.json ou l'environnement, sans
/// démarrer l'application web.
/// </summary>
public sealed class MlDbContextFactory : IDesignTimeDbContextFactory<MlDbContext>
{
    public MlDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(Path.Combine("SoftTime.Api", "appsettings.json"), optional: true)
            .Build();

        // Les variables d'environnement restent prioritaires pour les pipelines de déploiement.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SoftTimeMl");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = configuration.GetConnectionString("SoftTimeMl");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SoftTime");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = configuration.GetConnectionString("SoftTime");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Chaîne de connexion absente : renseignez ConnectionStrings:SoftTimeMl ou ConnectionStrings:SoftTime.");
        }

        var options = new DbContextOptionsBuilder<MlDbContext>()
            .UseSqlServer(connectionString, sql => sql
                .UseCompatibilityLevel(120)
                .EnableRetryOnFailure()
                .CommandTimeout(120))
            .Options;

        return new MlDbContext(options);
    }
}
