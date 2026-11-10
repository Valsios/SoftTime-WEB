namespace SoftTime.Application.DTOs;

/// <param name="TauxPonctualite">Jours travaillés sans retard / jours travaillés, en pourcentage.</param>
public record DashboardAssiduiteRowDto(
    string Matricule, string? Nom, string? Prenom, string? Departement, string? Categorie,
    int JoursTravailles, int JoursAbsence, int JoursRetard, int MinutesRetard, decimal TauxPonctualite)
{
    public DashboardAssiduiteRowDto() : this(string.Empty, default, default, default, default,
        default, default, default, default, default) { }
}
