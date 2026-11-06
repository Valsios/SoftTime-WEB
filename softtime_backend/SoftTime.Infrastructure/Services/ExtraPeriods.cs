using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services;

internal static class ExtraPeriods
{

    /// <summary>Refuse une période vide : <c>DateTime.MinValue</c> en bornes viderait toute la table.</summary>
    internal static void EnsurePeriode(PeriodRequest request)
    {
        if (request.From == default || request.To == default)
            throw new DomainException("Période requise (du … au …).");
        if (request.To.Date < request.From.Date)
            throw new DomainException("La date de fin doit suivre la date de début.");
    }
}
