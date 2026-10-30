using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Mapping;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(MappingProfile).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IPasswordHasher<T_RESPONSABLE>, PasswordHasher<T_RESPONSABLE>>();
        services.AddScoped<TenantConnectionService>();
        services.AddScoped<AuthService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<IExternalSourceService, ExternalSourceService>();
        services.AddScoped<SageLeaveCalendar>();
        services.AddScoped<PlanningService>();
        services.AddScoped<TimekeepingService>();
        services.AddScoped<PunchProcessingService>();
        services.AddScoped<HistoryArchiveService>();
        services.AddScoped<OvertimeService>();
        services.AddScoped<PayrollWriterResolver>();
        services.AddScoped<ReportService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ExtraService>();
        services.AddScoped<AuditService>();

        // Moteur prédictif : options liées à la section « Ml », services de la base analytique.
        services.AddOptions();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MlOptions>>().Value);
        services.AddScoped<MlDatasetService>();
        services.AddScoped<MlTrainingService>();
        services.AddScoped<MlPredictionService>();
        services.AddScoped<MlPipelineService>();
        services.AddSingleton<MlJobCoordinator>();
        return services;
    }
}
