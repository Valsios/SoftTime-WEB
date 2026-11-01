using SoftTime.Domain.Models;

namespace SoftTime.Application.DTOs;

/// <summary>
/// Demande d'analyse de clôture : période inclusive, département facultatif, page de la liste
/// d'anomalies et options des règles (valeurs par défaut du Domain si absentes).
/// </summary>
public sealed record ClosingAnalysisRequest(
    DateTime From,
    DateTime To,
    string? Departement = null,
    int? Page = null,
    int? PageSize = null,
    ClosingRulesOptions? Options = null);

/// <summary>Comptage d'une gravité ou d'un type d'anomalie.</summary>
public sealed record ClosingCountDto(string Cle, int Nombre);

/// <summary>Anomalie exposée : matricule canonique uniquement, aucun nom ni prénom.</summary>
public sealed record ClosingAnomalyDto(
    string Type,
    string Gravite,
    int Poids,
    string Matricule,
    string? Departement,
    DateTime Date,
    string Detail,
    decimal? Heures);

/// <summary>Score et répartition d'un département (<c>null</c> : salariés sans département).</summary>
public sealed record ClosingDepartmentDto(
    string? Departement,
    int? Score,
    int JoursEmployeControles,
    int NombreAnomalies,
    IReadOnlyList<ClosingCountDto> ParGravite,
    IReadOnlyList<ClosingCountDto> ParType);

/// <summary>Écart de comptage d'un type : <c>Ecart = Actuel − Precedent</c> (positif = plus d'anomalies).</summary>
public sealed record ClosingTypeDeltaDto(string Type, int Precedent, int Actuel, int Ecart);

/// <summary>
/// Comparaison avec la période précédente de même longueur. <c>EcartScore = Score − ScorePrecedent</c>
/// (positif = amélioration) ; <c>null</c> dès qu'un des deux scores est absent.
/// </summary>
public sealed record ClosingComparisonDto(
    DateTime From,
    DateTime To,
    int? ScorePrecedent,
    int JoursEmployeControlesPrecedent,
    int? EcartScore,
    IReadOnlyList<ClosingTypeDeltaDto> ParType);

/// <summary>Résultat de l'analyse de clôture d'une période.</summary>
public sealed record ClosingAnalysisDto(
    DateTime From,
    DateTime To,
    string? Departement,
    int? Score,
    string Formule,
    int JoursEmployeControles,
    int NombreAnomalies,
    IReadOnlyList<ClosingCountDto> ParGravite,
    IReadOnlyList<ClosingCountDto> ParType,
    PagedResult<ClosingAnomalyDto> Anomalies,
    IReadOnlyList<ClosingDepartmentDto> ParDepartement,
    ClosingComparisonDto? Comparaison);
