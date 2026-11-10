namespace SoftTime.Application.DTOs;

/// <summary>Anomalie du calcul cantine : le total du jour est contradictoire (pointage vs absence déclarée).</summary>
public record CanteenAnomalyDto(string? Matricule, DateTime Date, string Motif)
{
    public CanteenAnomalyDto() : this(default, default, string.Empty) { }
}
