using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Overtime;

public interface IOvertimeDetailService
{
    Task PurgeHsAsync(PeriodRequest request, CancellationToken ct = default);

    /// <summary>
    /// Détail du calcul des heures supplémentaires d'un salarié : jours issus des heures corrigées,
    /// répartition hebdomadaire EXO/IMPO et allocation enregistrée. Les jours passent par le même
    /// calculateur que la validation semaine, afin que l'écran de détail explique les valeurs payées.
    /// </summary>
    Task<OvertimeDetailDto> DetailAsync(string matricule, DateTime from, DateTime to, CancellationToken ct = default);
}
