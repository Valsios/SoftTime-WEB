using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Services;

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
