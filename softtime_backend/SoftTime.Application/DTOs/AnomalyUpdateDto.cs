namespace SoftTime.Application.DTOs;

/// <summary>
/// Arbitrage d'une anomalie : heures retenues et drapeaux d'absence / férié.
/// Les propriétés non renseignées (null) laissent la valeur existante inchangée.
/// </summary>
public record AnomalyUpdateDto(DateTime? DateIn, DateTime? DateOut, TimeSpan? HEntree, TimeSpan? HPs, TimeSpan? HPe, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, bool? Feries, bool? FeriesAm, bool? FeriesPm, string? IntituleAbsence)
{
    public AnomalyUpdateDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
}
