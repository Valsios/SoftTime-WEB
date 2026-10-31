using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.MachineLearning;
using SoftTime.Infrastructure.Persistence;
using SoftTime.Infrastructure.Queries;
using SoftTime.Infrastructure.Repositories;

namespace SoftTime.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SoftTimeDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("SoftTime"),
                // CommandTimeout relevé : les tables de pointages n'ont pas d'index couvrant le filtre
                // (base + plage de dates), donc les rapports balayent tout le fichier. Sur un SQL Server
                // dont le cache est froid, 30 s (défaut) suffisaient à faire échouer la page d'analyse.
                // CompatibilityLevel 120 : la base est en niveau 100 et EF Core 8 traduit par défaut
                // les Contains de collection en OPENJSON (niveau 130+). À 120, EF génère des listes IN,
                // compatibles avec la base existante et avec les requêtes de pagination.
                sql => sql
                    .UseCompatibilityLevel(120)
                    .EnableRetryOnFailure()
                    .CommandTimeout(120)));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<ISageContextFactory, SageContextFactory>();
        services.AddSingleton<IPointeuseContextFactory, PointeuseContextFactory>();
        services.AddScoped<ISagePayrollWriter, SagePayrollWriter>();
        services.AddScoped<IMappedSourceReader, MappedSourceReader>();
        services.AddScoped<IPayrollWriter, StandardPayrollWriter>();
        services.AddScoped<IPayrollWriter, OtherPayrollWriter>();
        services.AddScoped<IExternalDiscoveryService, ExternalDiscoveryService>();
        services.AddScoped<IPunchAggregate, PunchAggregate>();
        services.AddScoped<IPlanningStatisticsReader, PlanningStatisticsReader>();
        services.AddScoped<IEmailService, EmailService>();

        // Base analytique « model-ready » : schéma ml dans la base SoftTime par défaut,
        // ou base dédiée via ConnectionStrings:SoftTimeMl (aucun changement de code).
        var mlConnection = configuration.GetConnectionString("SoftTimeMl");
        if (string.IsNullOrWhiteSpace(mlConnection))
            mlConnection = configuration.GetConnectionString("SoftTime");

        services.AddDbContext<MlDbContext>(options =>
            options.UseSqlServer(
                mlConnection,
                sql => sql
                    .UseCompatibilityLevel(120)
                    .EnableRetryOnFailure()
                    .CommandTimeout(300)));
        services.AddScoped<IMlStore, EfMlStore>();
        services.AddSingleton<IMlTrainer, MlNetTrainer>();
        services.AddSingleton<IMlPredictor, MlNetPredictor>();
        return services;
    }
}
