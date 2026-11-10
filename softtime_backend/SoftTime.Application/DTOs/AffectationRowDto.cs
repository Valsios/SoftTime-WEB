namespace SoftTime.Application.DTOs;

public record AffectationRowDto(
    int Id,
    int CardPaieId,
    int CategoryId,
    string? Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    string? Service);
