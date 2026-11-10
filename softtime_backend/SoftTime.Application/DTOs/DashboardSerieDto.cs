namespace SoftTime.Application.DTOs;

/// <summary>Point d'une courbe d'évolution (une période = un regroupement).</summary>
public record DashboardSerieDto(string Periode, decimal Heures, decimal HeuresSupplementaires)
{
    public DashboardSerieDto() : this(string.Empty, default, default) { }
}
