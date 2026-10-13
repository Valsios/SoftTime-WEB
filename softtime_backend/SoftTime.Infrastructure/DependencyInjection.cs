using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Integrations;
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
                sql => sql.EnableRetryOnFailure().CommandTimeout(120)));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<ISageContextFactory, SageContextFactory>();
        services.AddSingleton<IPointeuseContextFactory, PointeuseContextFactory>();
        services.AddScoped<ISagePayrollWriter, SagePayrollWriter>();
        services.AddScoped<IMappedSourceReader, MappedSourceReader>();
        services.AddScoped<IPayrollWriter, StandardPayrollWriter>();
        services.AddScoped<IPayrollWriter, OtherPayrollWriter>();
        services.AddScoped<IExternalDiscoveryService, ExternalDiscoveryService>();
        services.AddScoped<IPunchAggregate, PunchAggregate>();
        services.AddScoped<IEmailService, EmailService>();
        return services;
    }
}
