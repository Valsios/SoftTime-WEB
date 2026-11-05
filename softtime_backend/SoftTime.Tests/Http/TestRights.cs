using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Application.Security;
using SoftTime.Domain.Repositories;

namespace SoftTime.Tests.Http;

/// <summary>Service de droits effectifs pour les tests unitaires : cache vide propre à chaque appel, journal muet.</summary>
public static class TestRights
{
    public static EffectiveRightsService Service(IUnitOfWork uow, EffectiveRightsCache? cache = null)
        => new(uow, cache ?? new EffectiveRightsCache(), NullLogger<EffectiveRightsService>.Instance);
}
