namespace SoftTime.Application.DTOs;

/// <summary>Répartition de l'effectif et de l'activité d'un axe (catégorie, département).</summary>
public record DashboardSliceDto(string Libelle, int Effectif, int Absences, int Retards, int MinutesRetard, decimal HeuresSupplementaires)
{
    public DashboardSliceDto() : this(string.Empty, default, default, default, default, default) { }
}
