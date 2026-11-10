using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Reports;

public interface IReportContextLoader
{
    /// <summary>
    /// Contexte partagé par l'état des absences et l'état des retards : cartes filtrées (base SAGE,
    /// plage de matricules, département), fériés, congés SAGE, corrections, plannings, affectations de
    /// catégorie et tolérances. La fenêtre est plafonnée à la veille : aucun état ne fabrique
    /// d'événement futur, exactement comme le tableau de bord.
    /// </summary>
    Task<ReportContext?> ChargerContexteAsync(ReportFilter filter, CancellationToken ct);
}
