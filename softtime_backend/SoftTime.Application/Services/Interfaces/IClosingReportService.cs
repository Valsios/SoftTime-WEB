using SoftTime.Application.DTOs;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Cas d'usage « analyse de clôture » de l'écran : contrôle d'accès, analyse puis présentation.</summary>
public interface IClosingReportService
{
    /// <summary>
    /// Rapport de clôture de la période (mois civil précédent sans dates) ; <see cref="NotFoundException"/> si la
    /// fonctionnalité est désactivée, <see cref="ForbiddenException"/> sans la permission <c>REPORTS:CLOSING</c>.
    /// </summary>
    Task<ClosingReportDto> AnalyseAsync(ClosingAnalysisQuery query, CancellationToken ct = default);
}
