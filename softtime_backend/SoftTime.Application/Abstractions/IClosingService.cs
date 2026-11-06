using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Abstractions;

/// <summary>
/// Analyse de clôture de paie, en lecture seule : charge en une passe (ensembliste) le planning,
/// les pointages, les corrections, les semaines validées, les fériés et les congés SAGE d'une
/// période et de la période précédente de même longueur, puis applique toutes les règles de
/// <see cref="PayrollClosingRules"/>. Aucune écriture en base, aucun appel au journal.
///
/// Modèle d'orchestration : <see cref="IDashboardService"/> (base active et droits via
/// <see cref="ITenantConnectionService.EnsureAuthorizedAsync"/>, congés via
/// <see cref="ISageLeaveCalendar"/>, filtre département appliqué aux cartes avant tout calcul).
///
/// Conventions :
/// <list type="bullet">
///   <item>le jour en cours et les jours futurs ne sont jamais contrôlés : la période est plafonnée
///     à la veille (une journée entamée n'est pas encore complète) ;</item>
///   <item>un jour-employé contrôlé est un couple (salarié, jour) de la période dont le planning
///     retenu est un jour travaillé avec heures, ou qui porte au moins un pointage ;</item>
///   <item>seuls des matricules (clé canonique) sont manipulés : aucun nom ni prénom.</item>
/// </list>
/// </summary>
public interface IClosingService
{
    /// <summary>
    /// Analyse d'une période. Sans options de règles dans la demande, les réglages « Closing »
    /// s'appliquent (tolérance, semaines validées, gravités, poids, seuil de statut).
    /// </summary>
    Task<ClosingAnalysisDto> AnalyserAsync(ClosingAnalysisRequest request, CancellationToken ct = default);
}
