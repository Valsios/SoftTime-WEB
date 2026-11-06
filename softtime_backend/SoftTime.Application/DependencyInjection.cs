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
        services.AddScoped<TenantConnectionService>();
        services.AddScoped<AuthService>();
        services.AddSingleton<EffectiveRightsCache>();
        services.AddScoped<EffectiveRightsService>();
        services.AddScoped<DatabaseConfigurationAuthorizer>();
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
        services.AddScoped<JournalService>();

        // Moteur prédictif : options liées à la section « Ml », services de la base analytique.
        services.AddOptions();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MlOptions>>().Value);
        services.AddScoped<MlDatasetService>();
        services.AddScoped<MlTrainingService>();
        services.AddScoped<MlPredictionService>();
        services.AddScoped<MlPipelineService>();
        services.AddSingleton<MlJobCoordinator>();

        // Clôture de paie assistée : réglages de la section « Closing » (valeurs par défaut en code),
        // refus du démarrage si un réglage est invalide ; analyse et présentation en lecture seule.
        services.AddOptions<ClosingOptions>()
            .Configure<Microsoft.Extensions.Configuration.IConfiguration>((o, configuration) => o.Lire(configuration.GetSection(ClosingOptions.Section)))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ClosingOptions>, ClosingOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ClosingOptions>>().Value);
        services.AddScoped<ClosingService>();
        services.AddScoped<ClosingPresentationService>();

        // Mon espace salarié (lecture seule) : désactivé par défaut (SelfService:Enabled = false).
        services.AddOptions<SoftTime.Application.SelfService.SelfServiceOptions>()
            .Configure<Microsoft.Extensions.Configuration.IConfiguration>((o, configuration) =>
            {
                if (bool.TryParse(configuration[SoftTime.Application.SelfService.SelfServiceOptions.Section + ":Enabled"], out var actif))
                    o.Enabled = actif;
            });
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SoftTime.Application.SelfService.SelfServiceOptions>>().Value);
        services.AddSingleton<SoftTime.Application.SelfService.SelfLeaveCache>();
        services.AddScoped<SoftTime.Application.SelfService.SelfServiceGuard>();
        services.AddScoped<SoftTime.Application.SelfService.SelfSpaceService>();
        return services;
    }
}
