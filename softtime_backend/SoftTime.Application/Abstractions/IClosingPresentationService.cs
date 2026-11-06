using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Abstractions;

/// <summary>
/// Présentation de l'analyse de clôture : ajoute nom et prénom aux anomalies de la page courante,
/// depuis <c>T_CARDPAIE</c> de la base active (clé <see cref="MatriculeIdentity.Cle"/>, carte de plus
/// petit ID comme les états), en UNE requête ensembliste. Avec <c>Closing:ShowNames = false</c>,
/// aucune lecture : matricule seul, nom et prénom <c>null</c>. Lecture seule.
/// </summary>
public interface IClosingPresentationService
{
    Task<ClosingReportDto> PresenterAsync(ClosingAnalysisDto analyse, CancellationToken ct = default);
}
