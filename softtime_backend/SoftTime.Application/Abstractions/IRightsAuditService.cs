using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IRightsAuditService
{
    Task<IReadOnlyList<AuditDroitDto>> GetAuditDroitsAsync(string? typeCible, decimal? idCible, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
}
