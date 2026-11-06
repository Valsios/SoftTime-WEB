using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Domain.Models;

/// <summary>Catégorie de chaque carte (<c>T_CAT_SAL</c>) et tolérances de retard par catégorie.</summary>
public sealed record RetardTolerances(IReadOnlyDictionary<int, int> CategorieParCarte, IReadOnlyList<ToleranceRetard> Tolerances);
