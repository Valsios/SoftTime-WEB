using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Mapping;
using SoftTime.Application.Security;
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
        services.AddScoped<ITenantConnectionService, TenantConnectionService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<EffectiveRightsCache>();
        services.AddScoped<IEffectiveRightsService, EffectiveRightsService>();
        services.AddScoped<IDatabaseConfigurationAuthorizer, DatabaseConfigurationAuthorizer>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IDatabaseListingService, DatabaseListingService>();
        services.AddScoped<IExternalSourceService, ExternalSourceService>();
        services.AddScoped<ISageLeaveCalendar, SageLeaveCalendar>();
        services.AddScoped<IPlanningService, PlanningService>();
        services.AddScoped<ITimekeepingService, TimekeepingService>();
        services.AddScoped<IPunchProcessingService, PunchProcessingService>();
        services.AddScoped<IHistoryArchiveService, HistoryArchiveService>();
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IPayrollWriterResolver, PayrollWriterResolver>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IExtraService, ExtraService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IJournalService, JournalService>();

        // Moteur prédictif : options liées à la section « Ml », services de la base analytique.
        services.AddOptions();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MlOptions>>().Value);
        services.AddScoped<IMlDatasetService, MlDatasetService>();
        services.AddScoped<IMlTrainingService, MlTrainingService>();
        services.AddScoped<IMlPredictionService, MlPredictionService>();
        services.AddScoped<IMlPipelineService, MlPipelineService>();
        services.AddSingleton<IMlJobCoordinator, MlJobCoordinator>();
        services.AddScoped<IPredictionsService, PredictionsService>();

        // Clôture de paie assistée : réglages de la section « Closing » (valeurs par défaut en code),
        // refus du démarrage si un réglage est invalide ; analyse et présentation en lecture seule.
        services.AddOptions<ClosingOptions>()
            .Configure<Microsoft.Extensions.Configuration.IConfiguration>((o, configuration) => o.Lire(configuration.GetSection(ClosingOptions.Section)))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ClosingOptions>, ClosingOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ClosingOptions>>().Value);
        services.AddScoped<IClosingService, ClosingService>();
        services.AddScoped<IClosingPresentationService, ClosingPresentationService>();
        services.AddScoped<IClosingReportService, ClosingReportService>();
        return services;
    }
}
