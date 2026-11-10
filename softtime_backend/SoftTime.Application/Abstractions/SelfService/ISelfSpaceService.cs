using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.SelfService;

/// <summary>
/// Lecture seule des données du salarié connecté (« Mon espace ») : toute lecture est filtrée sur le matricule résolu
/// par <see cref="ISelfServiceGuard"/>, donc sur le jeton.
/// </summary>
public interface ISelfSpaceService
{
    Task<SelfSummaryDto> SummaryAsync(SelfPeriodQuery query, CancellationToken ct = default);

    Task<SelfWeeksDto> WeeksAsync(SelfPeriodQuery query, CancellationToken ct = default);

    Task<SelfAnomaliesDto> AnomaliesAsync(SelfPeriodQuery query, CancellationToken ct = default);

    Task<SelfAbsencesDto> AbsencesAsync(SelfPeriodQuery query, CancellationToken ct = default);

    Task<SelfDelaysDto> DelaysAsync(SelfPeriodQuery query, CancellationToken ct = default);
}
