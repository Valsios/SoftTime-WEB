namespace SoftTime.Application.DTOs;

public record AuditDroitDto(
    int Id,
    DateTime DateAction,
    decimal IdOperateur,
    string LoginOperateur,
    string TypeCible,
    decimal IdCible,
    string? NomCible,
    string Action,
    string? PermsAvant,
    string? PermsApres
);
