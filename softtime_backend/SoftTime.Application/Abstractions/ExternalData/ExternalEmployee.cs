namespace SoftTime.Application.Abstractions.ExternalData;

public sealed record ExternalEmployee(
    string Matricule,
    long EmployeeId,
    string? Nom,
    string? Prenom,
    string? Badge,
    bool Inactive,
    byte? Civilite = null);
