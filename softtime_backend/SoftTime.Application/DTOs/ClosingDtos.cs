namespace SoftTime.Application.DTOs;

/// <summary>
/// Demande d'analyse de clôture : période inclusive, département facultatif, page de la liste
/// d'anomalies et options des règles (valeurs par défaut du Domain si absentes).
/// Sans <see cref="From"/> ni <see cref="To"/>, la période est le mois civil complet précédent.
/// <see cref="IncludeComparison"/> (faux par défaut) charge et calcule aussi la période précédente
/// de même longueur ; sinon <c>Comparaison</c> vaut <c>null</c>. <see cref="IncludeInformation"/>
/// (faux par défaut) ajoute à la LISTE les anomalies « Information » ; les comptages les incluent toujours.
/// <see cref="ReuseCached"/> (faux par défaut) autorise la réutilisation, pendant un court instant, de l'analyse
/// déjà calculée pour la même période : réservé aux changements de page, jamais à une nouvelle analyse.
/// </summary>
public sealed record ClosingAnalysisRequest(
    DateTime From,
    DateTime To,
    string? Departement = null,
    int? Page = null,
    int? PageSize = null,
    ClosingRulesOptions? Options = null,
    bool IncludeComparison = false,
    bool IncludeInformation = false,
    bool ReuseCached = false);

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
    decimal? Heures,
    bool? PointageExiste = null);

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
    ClosingComparisonDto? Comparaison,
    string? Statut = null,
    int SalariesSansPlanningExclus = 0);

/// <summary>Anomalie présentée à l'écran : nom et prénom <c>null</c> quand l'affichage des noms est désactivé.</summary>
public sealed record ClosingAnomalyViewDto(
    string Type,
    string Gravite,
    int Poids,
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    DateTime Date,
    string Detail,
    decimal? Heures,
    bool? PointageExiste = null);

/// <summary>
/// Réponse de la route d'analyse : statut (Pret / ACorriger / Bloquant, <c>null</c> sans jour
/// contrôlé) et son libellé, seuil appliqué, score, formule, comptages, départements, page
/// d'anomalies enrichie, comparaison (si demandée).
/// </summary>
public sealed record ClosingReportDto(
    DateTime From,
    DateTime To,
    string? Departement,
    string? Statut,
    string? StatutLibelle,
    decimal SeuilBloquant,
    int? Score,
    string Formule,
    int JoursEmployeControles,
    int NombreAnomalies,
    IReadOnlyList<ClosingCountDto> ParGravite,
    IReadOnlyList<ClosingCountDto> ParType,
    PagedResult<ClosingAnomalyViewDto> Anomalies,
    IReadOnlyList<ClosingDepartmentDto> ParDepartement,
    ClosingComparisonDto? Comparaison,
    bool NomsAffiches,
    int SalariesSansPlanningExclus = 0);
