using AutoMapper;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services.DataSources;
using SoftTime.Infrastructure.Services.Employees;
using SoftTime.Infrastructure.Services.Settings;

namespace SoftTime.Tests;

/// <summary>
/// Assemble les services du catalogue (ex-CatalogService) avec les mêmes dépendances que l'ancien constructeur,
/// pour les tests qui construisent les services à la main.
/// </summary>
internal sealed class CatalogServices
{
    public CatalogServices(
        IUnitOfWork uow,
        IMapper mapper,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        IExternalDiscoveryService discovery,
        IDatabaseConfigurationAuthorizer configAuth,
        IEffectiveRightsService effectiveRights)
    {
        var mappingStore = new SourceMappingStore(uow, mapper, configAuth);
        var mappingMigrator = new SourceMappingMigrator(uow);
        var mappingValidator = new SourceMappingValidator(uow, discovery);
        var categories = new CategoryService(uow, mapper, tenant);
        var departements = new DepartementServiceLookup(tenant, externalSource);
        var correspondence = new CorrespondenceModeService(uow, tenant, externalSource);

        Affectations = new AffectationService(uow, tenant, categories, departements);
        AffectationImports = new AffectationImportService(uow, tenant);
        Cards = new CardPaieService(uow, mapper, tenant, correspondence);
        Discovery = new DiscoveryService(discovery, mappingStore);
        Holidays = new HolidayService(uow, mapper, tenant, externalSource);
        CodeConstantes = new CodeConstanteService(uow, mapper, tenant, externalSource, discovery);
        Pointeuse = new PointeuseDatabaseService(
            uow, mapper, discovery, configAuth, effectiveRights, mappingStore, mappingMigrator, mappingValidator);
        Sage = new SageDatabaseService(
            uow, mapper, tenant, discovery, configAuth, effectiveRights, Holidays, CodeConstantes, Cards,
            mappingStore, mappingMigrator, mappingValidator);
    }

    public AffectationService Affectations { get; }

    public AffectationImportService AffectationImports { get; }

    public CardPaieService Cards { get; }

    public DiscoveryService Discovery { get; }

    public HolidayService Holidays { get; }

    public CodeConstanteService CodeConstantes { get; }

    public PointeuseDatabaseService Pointeuse { get; }

    public SageDatabaseService Sage { get; }
}
