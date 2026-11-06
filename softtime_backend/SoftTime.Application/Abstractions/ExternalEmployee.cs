namespace SoftTime.Application.Abstractions;

public sealed record ExternalEmployee(
    string Matricule,
    long EmployeeId,
    string? Nom,
    string? Prenom,
    string? Badge,
    bool Inactive,
    byte? Civilite = null);
