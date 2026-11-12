namespace SoftTime.Application.DTOs;

/// <summary>Salarié présenté à l'écran : nom et prénom <c>null</c> quand l'affichage des noms est désactivé.</summary>
public sealed record ClosingEmployeeViewDto(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    int NombreAnomalies,
    int Poids,
    IReadOnlyList<ClosingCountDto> ParGravite,
    IReadOnlyList<ClosingCountDto> ParType);
