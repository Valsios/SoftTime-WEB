using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

/// <summary>Montants d'heures supplémentaires d'un salarié à reporter dans les cumuls SAGE.</summary>
public sealed record OvertimeAmounts(
    decimal Exo130, decimal Exo150, decimal I130, decimal I150, decimal Ferie, decimal Nuit, decimal Dim)
{
    /// <summary>Montants d'une ligne EXO/IMPO, une valeur absente valant zéro.</summary>
    public static OvertimeAmounts From(HsExoDto hs) => new(
        hs.Exo130 ?? 0, hs.Exo150 ?? 0, hs.I130 ?? 0, hs.I150 ?? 0, hs.Ferie ?? 0, hs.Nuit ?? 0, hs.Dim ?? 0);
}
