namespace SoftTime.Application.DTOs;

/// <summary>
/// Résultat de l'analyse de clôture d'une période. <see cref="ParSalarie"/> : les salariés les plus touchés
/// (plafonnés), <see cref="SalariesConcernes"/> : nombre total de salariés ayant au moins une anomalie ; ni l'un ni
/// l'autre ne dépend des filtres de la liste.
/// </summary>
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
    int SalariesSansPlanningExclus = 0,
    IReadOnlyList<ClosingEmployeeDto>? ParSalarie = null,
    int SalariesConcernes = 0);
