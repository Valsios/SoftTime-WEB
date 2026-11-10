namespace SoftTime.Application.DTOs;

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
