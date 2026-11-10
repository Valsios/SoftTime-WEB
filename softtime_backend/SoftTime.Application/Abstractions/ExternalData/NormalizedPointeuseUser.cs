namespace SoftTime.Application.Abstractions.ExternalData;

 
// La fiche "Utilisateur pointeuse" (qui a quel badge)
public record NormalizedPointeuseUser(
    string Id,
    string? Badge,
    string? Ssn,
    string? Nom);
