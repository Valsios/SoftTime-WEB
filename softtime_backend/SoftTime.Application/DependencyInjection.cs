using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;

namespace SoftTime.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(MappingProfile).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<TenantConnectionService>();
        services.AddScoped<AuthService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<PlanningService>();
        services.AddScoped<TimekeepingService>();
        services.AddScoped<OvertimeService>();
        services.AddScoped<ReportService>();
        services.AddScoped<ExtraService>();
        services.AddScoped<AuditService>();
        return services;
    }
}
