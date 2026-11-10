using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>Analyse complète d'une période, sans la page de la liste (réutilisable d'une page à l'autre).</summary>
internal sealed record AnalysePreparee(
    ClosingRulesOptions Options,
    DateTime From,
    DateTime To,
    IReadOnlyDictionary<string, string?> DepartementParCle,
    ResultatPeriode Actuel,
    int? Score,
    ClosingComparisonDto? Comparaison,
    IReadOnlyList<ClosingDepartmentDto> ParDepartement);

