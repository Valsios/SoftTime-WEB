using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Employees;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Ml;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Services;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services.Ml;

namespace SoftTime.Tests;

/// <summary>Construit MlDatasetService et son chargeur de contexte avec les dépendances de l'ancien constructeur.</summary>
internal static class MlDatasetTestFactory
{
    public static MlDatasetService Create(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        ISageLeaveCalendar leaves,
        IExternalSourceService externalSource,
        IMlStore store,
        IOptions<MlOptions> options,
        ILogger<MlDatasetService> logger)
        => new(store, options, logger, new MlDatasetContextLoader(uow, tenant, leaves, externalSource, options));
}
