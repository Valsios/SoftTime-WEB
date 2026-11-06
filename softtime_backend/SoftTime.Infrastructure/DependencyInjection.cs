using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.Security;
using SoftTime.Infrastructure.MachineLearning;
using SoftTime.Infrastructure.Persistence;
using SoftTime.Infrastructure.Queries;
using SoftTime.Infrastructure.Repositories;
using SoftTime.Infrastructure.SchemaMigrations;

namespace SoftTime.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // L'API remplace ces contextes par les valeurs extraites du JWT et du tenant.
        // Les valeurs neutres permettent aussi de construire l'infrastructure seule
        // (outils et tests) sans attribuer une identité applicative fictive.
        services.TryAddScoped<ICompanyContext, EmptyCompanyContext>();
        services.TryAddScoped<ICurrentUser, EmptyCurrentUser>();
        services.AddSingleton(AuditJournalOptions.From(configuration));
        services.AddScoped<AuditJournalSaveChangesInterceptor>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddDbContext<SoftTimeDbContext>((serviceProvider, options) =>
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
                    .CommandTimeout(120))
            .AddInterceptors(serviceProvider.GetRequiredService<AuditJournalSaveChangesInterceptor>()));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Migrations SQL versionnées (migrations/versions), exécutées au démarrage si Database:AutoMigrate.
        services.AddSingleton(SchemaMigrationOptions.FromConfiguration(configuration));
        services.AddSingleton<IMigrationScriptSource>(_ => new EmbeddedMigrationScriptSource());
        services.AddSingleton<ISchemaMigrationConnectionFactory>(_ =>
            new SqlSchemaMigrationConnectionFactory(configuration.GetConnectionString("SoftTime") ?? string.Empty));
        services.AddSingleton<SchemaMigrator>();
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
        // BCrypt remplace le hacheur Identity enregistré par AddApplication (les anciens hachages migrent à la connexion).
        services.Replace(ServiceDescriptor.Scoped<IPasswordHasher<T_RESPONSABLE>, BcryptPasswordHasher>());

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

    private sealed class EmptyCompanyContext : ICompanyContext
    {
        public string? SageDatabase => null;
        public string? PointeuseDatabase => null;
        public void Set(string sage, string? pointeuse) { }
    }

    private sealed class EmptyCurrentUser : ICurrentUser
    {
        public decimal? UserId => null;
        public string? Login => null;
        public decimal? RoleId => null;
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights => Array.Empty<int>();
        public bool HasRight(int droit) => false;
    }
}
