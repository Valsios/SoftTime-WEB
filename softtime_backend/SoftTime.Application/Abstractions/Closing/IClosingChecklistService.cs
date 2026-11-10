using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Closing;

public interface IClosingChecklistService
{
    /// <summary>
    /// Checklist d'avancement de la paie (lecture seule) pour la période (mois civil précédent sans dates) et le
    /// département demandés. Séparée de l'analyse : elle n'entre ni dans le score ni dans le statut.
    /// </summary>
    Task<ClosingChecklistDto> ChecklistAsync(ClosingChecklistRequest request, CancellationToken ct = default);
}
