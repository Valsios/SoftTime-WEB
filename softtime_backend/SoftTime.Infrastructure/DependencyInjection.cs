using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SoftTime.Application.Abstractions.Audit;
using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.Abstractions.Dashboard;
using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.EmployeePlanning;
using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.Abstractions.Excel;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Extras;
using SoftTime.Application.Abstractions.History;
using SoftTime.Application.Abstractions.Ml;
using SoftTime.Application.Abstractions.Notifications;
using SoftTime.Application.Abstractions.Overtime;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.PlanningImport;
using SoftTime.Application.Abstractions.Punches;
using SoftTime.Application.Abstractions.PunchProcessing;
using SoftTime.Application.Abstractions.Reports;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.Abstractions.Users;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.MachineLearning;
using SoftTime.Infrastructure.Persistence;
using SoftTime.Infrastructure.Queries;
using SoftTime.Infrastructure.Repositories;
using SoftTime.Infrastructure.SchemaMigrations;
using SoftTime.Infrastructure.Security;
using SoftTime.Infrastructure.Services.Audit;
using SoftTime.Infrastructure.Services.Authentication;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Dashboard;
using SoftTime.Infrastructure.Services.DataSources;
using SoftTime.Infrastructure.Services.EmployeePlanning;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.ExternalData;
using SoftTime.Infrastructure.Services.Extras;
using SoftTime.Infrastructure.Services.History;
using SoftTime.Infrastructure.Services.Ml;
using SoftTime.Infrastructure.Services.Overtime;
using SoftTime.Infrastructure.Services.Payroll;
using SoftTime.Infrastructure.Services.Permissions;
using SoftTime.Infrastructure.Services.PlanningImport;
using SoftTime.Infrastructure.Services.Punches;
using SoftTime.Infrastructure.Services.PunchProcessing;
using SoftTime.Infrastructure.Services.Reports;
using SoftTime.Infrastructure.Services.RequestContext;
using SoftTime.Infrastructure.Services.Settings;
using SoftTime.Infrastructure.Services.Users;

namespace SoftTime.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Options liées à la configuration (§5.6) : sections « AuditJournal », « Database » et « Email ».
    /// Les valeurs absentes gardent leur défaut ; une valeur hors bornes est ramenée au défaut.
    /// </summary>
    public static IServiceCollection AddInfrastructureOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuditJournalOptions>()
            .Bind(configuration.GetSection(AuditJournalOptions.Section))
            .PostConfigure(options => options.Normalize());
        services.AddOptions<SchemaMigrationOptions>()
            .Bind(configuration.GetSection(SchemaMigrationOptions.Section))
            .PostConfigure(options => options.Normalize());
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section));
        return services;
    }

    /// <summary>Services applicatifs : contrats dans Application, implémentations ici (§4.4).</summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddAccountServices();
        services.AddCatalogServices();
        services.AddTimekeepingServices();
        services.AddPayrollServices();
        services.AddReportingServices();
        services.AddMachineLearningServices();
        services.AddClosingServices();
        return services;
    }

    /// <summary>Comptes, authentification et droits.</summary>
    private static void AddAccountServices(this IServiceCollection services)
    {
        services.AddScoped<ITenantConnectionService, TenantConnectionService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IFirstRunService, FirstRunService>();
        services.AddScoped<IUserDirectoryService, UserDirectoryService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IUserDeletionService, UserDeletionService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IPrivilegeService, PrivilegeService>();
        services.AddScoped<IParametersAccessGuard, ParametersAccessGuard>();
        services.AddScoped<IUserPermissionService, UserPermissionService>();
        services.AddScoped<IRolePermissionService, RolePermissionService>();
        services.AddScoped<IDatabaseAccessService, DatabaseAccessService>();
        services.AddScoped<IRightsAuditService, RightsAuditService>();
        services.AddScoped<IRightsChangeJournal, RightsChangeJournal>();
        services.AddScoped<IEffectiveRightsService, EffectiveRightsService>();
        services.AddScoped<IDatabaseConfigurationAuthorizer, DatabaseConfigurationAuthorizer>();
    }

    /// <summary>Paramétrage : référentiels, bases externes et mappings des sources.</summary>
    private static void AddCatalogServices(this IServiceCollection services)
    {
        services.AddScoped<IAbsenceCodeService, AbsenceCodeService>();
        services.AddSingleton<IExcelWorkbook, SoftTime.Infrastructure.Excel.ExcelWorkbook>();
        services.AddScoped<IAffectationService, AffectationService>();
        services.AddScoped<IAffectationImportService, AffectationImportService>();
        services.AddScoped<ICardPaieService, CardPaieService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IClockParameterService, ClockParameterService>();
        services.AddScoped<ICodeConstanteService, CodeConstanteService>();
        services.AddScoped<ICorrespondenceModeService, CorrespondenceModeService>();
        services.AddScoped<IDiscoveryService, DiscoveryService>();
        services.AddScoped<IFieldRoleService, FieldRoleService>();
        services.AddScoped<IHolidayService, HolidayService>();
        services.AddScoped<IMajorationService, MajorationService>();
        services.AddScoped<IPayrollWriteConfigurationService, PayrollWriteConfigurationService>();
        services.AddScoped<IPointeuseDatabaseService, PointeuseDatabaseService>();
        services.AddScoped<ISageDatabaseService, SageDatabaseService>();
        services.AddScoped<IDepartementServiceLookup, DepartementServiceLookup>();
        services.AddScoped<IToleranceService, ToleranceService>();
        services.AddScoped<ISourceMappingStore, SourceMappingStore>();
        services.AddScoped<ISourceMappingValidator, SourceMappingValidator>();
        services.AddScoped<ISourceMappingMigrator, SourceMappingMigrator>();
        services.AddScoped<IExternalSourceService, ExternalSourceService>();
        services.AddScoped<ISageLeaveCalendar, SageLeaveCalendar>();
    }

    /// <summary>Planning, pointages, anomalies, heures corrigées et archives.</summary>
    private static void AddTimekeepingServices(this IServiceCollection services)
    {
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IEmployeePlanningService, EmployeePlanningService>();
        services.AddScoped<IPlanningImportService, PlanningImportService>();
        services.AddScoped<IPlanningDatabaseFilter, PlanningDatabaseFilter>();
        services.AddScoped<IPunchService, PunchService>();
        services.AddScoped<IPunchImportService, PunchImportService>();
        services.AddScoped<IAnomalyService, AnomalyService>();
        services.AddScoped<ICorrectedHoursService, CorrectedHoursService>();
        services.AddScoped<IValidatedWeekGuard, ValidatedWeekGuard>();
        services.AddScoped<IPunchProcessingService, PunchProcessingService>();
        services.AddScoped<IHistoryArchiveService, HistoryArchiveService>();
        services.AddScoped<IHistoryQueryService, HistoryQueryService>();
        services.AddScoped<IHistoryPurgeService, HistoryPurgeService>();
    }

    /// <summary>Heures supplémentaires, cantine, frais et écriture de la paie.</summary>
    private static void AddPayrollServices(this IServiceCollection services)
    {
        services.AddScoped<IWeeklyValidationService, WeeklyValidationService>();
        services.AddScoped<IOvertimeExoImpoService, OvertimeExoImpoService>();
        services.AddScoped<IOvertimeDetailService, OvertimeDetailService>();
        services.AddScoped<IOvertimePayrollSyncService, OvertimePayrollSyncService>();
        services.AddScoped<IOvertimeWeekContext, OvertimeWeekContext>();
        services.AddScoped<IPayrollWriterResolver, PayrollWriterResolver>();
        services.AddScoped<ICanteenService, CanteenService>();
        services.AddScoped<ITravelExpenseService, TravelExpenseService>();
        services.AddScoped<IPayrollCumulSync, PayrollCumulSync>();
    }

    /// <summary>Rapports, tableau de bord, audit et journal.</summary>
    private static void AddReportingServices(this IServiceCollection services)
    {
        services.AddScoped<IPointageReportService, PointageReportService>();
        services.AddScoped<IAbsenceReportService, AbsenceReportService>();
        services.AddScoped<IRetardReportService, RetardReportService>();
        services.AddScoped<IHoursReportService, HoursReportService>();
        services.AddScoped<IReportContextLoader, ReportContextLoader>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IJournalService, JournalService>();
    }

    /// <summary>Moteur prédictif.</summary>
    private static void AddMachineLearningServices(this IServiceCollection services)
    {
        services.AddScoped<IMlDatasetContextLoader, MlDatasetContextLoader>();
        services.AddScoped<IMlDatasetService, MlDatasetService>();
        services.AddScoped<IMlTrainingService, MlTrainingService>();
        services.AddScoped<IMlPredictionService, MlPredictionService>();
        services.AddScoped<IMlForecastQueryService, MlForecastQueryService>();
        services.AddScoped<IMlPipelineService, MlPipelineService>();
    }

    /// <summary>Clôture de paie assistée.</summary>
    private static void AddClosingServices(this IServiceCollection services)
    {
        services.AddSingleton<ClosingAnalysisCache>();
        services.AddScoped<IClosingService, ClosingService>();
        services.AddScoped<IClosingPresentationService, ClosingPresentationService>();
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInfrastructureServices();
        // L'API remplace ces contextes par les valeurs extraites du JWT et du tenant.
        // Les valeurs neutres permettent aussi de construire l'infrastructure seule
        // (outils et tests) sans attribuer une identité applicative fictive.
        services.TryAddScoped<ICompanyContext, EmptyCompanyContext>();
        services.TryAddScoped<ICurrentUser, EmptyCurrentUser>();
        services.AddInfrastructureOptions(configuration);
        services.AddPersistence(configuration);
        services.AddSchemaMigrations(configuration);
        services.AddIntegrations();
        services.AddMachineLearningStorage(configuration);
        return services;
    }

    /// <summary>Contexte SoftTime (journal d'audit intercepté) et unité de travail.</summary>
    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
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
    }

    /// <summary>Migrations SQL versionnées (migrations/versions), exécutées au démarrage si Database:AutoMigrate.</summary>
    private static void AddSchemaMigrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IMigrationScriptSource>(_ => new EmbeddedMigrationScriptSource());
        services.AddSingleton<ISchemaMigrationConnectionFactory>(_ =>
            new SqlSchemaMigrationConnectionFactory(configuration.GetConnectionString("SoftTime") ?? string.Empty));
        services.AddSingleton<SchemaMigrator>();
    }

    /// <summary>Sources externes (SAGE, pointeuse), écritures de paie, courriel et hachage des mots de passe.</summary>
    private static void AddIntegrations(this IServiceCollection services)
    {
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
        services.Replace(ServiceDescriptor.Scoped<IPasswordHasher<Responsable>, BcryptPasswordHasher>());
    }

    /// <summary>Base analytique ML et moteur d'entraînement / prédiction.</summary>
    private static void AddMachineLearningStorage(this IServiceCollection services, IConfiguration configuration)
    {
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
