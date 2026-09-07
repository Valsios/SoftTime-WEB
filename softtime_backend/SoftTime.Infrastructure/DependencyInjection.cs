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
                sql => sql.EnableRetryOnFailure()));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<ISageContextFactory, SageContextFactory>();
        services.AddSingleton<IPointeuseContextFactory, PointeuseContextFactory>();
        services.AddScoped<ISagePayrollWriter, SagePayrollWriter>();
        services.AddScoped<IPunchAggregate, PunchAggregate>();
        return services;
    }
}
