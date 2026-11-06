namespace SoftTime.Application.DTOs;

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
