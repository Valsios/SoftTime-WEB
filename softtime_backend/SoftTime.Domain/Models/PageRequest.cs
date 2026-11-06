namespace SoftTime.Domain.Models;

/// <summary>Page demandée par le client ; valeurs absentes ou invalides normalisées par <c>PaginationRules</c>.</summary>
public sealed record PageRequest(int? Page, int? PageSize);
