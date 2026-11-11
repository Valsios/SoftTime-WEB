namespace SoftTime.Application.DTOs;

/// <summary>
/// Réponse de la route d'analyse : statut (Pret / ACorriger / Bloquant, <c>null</c> sans jour
/// contrôlé) et son libellé, seuil appliqué, score, formule, comptages, départements, page
/// d'anomalies enrichie, comparaison (si demandée), salariés les plus touchés (avec noms selon le réglage).
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
    int SalariesSansPlanningExclus = 0,
    IReadOnlyList<ClosingEmployeeViewDto>? ParSalarie = null,
    int SalariesConcernes = 0);
