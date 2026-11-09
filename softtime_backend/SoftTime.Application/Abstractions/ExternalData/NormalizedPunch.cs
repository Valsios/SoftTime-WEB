namespace SoftTime.Application.Abstractions.ExternalData;

 
// La fiche "Pointage" (qui a pointe, a quelle heure)
public record NormalizedPunch(
    string UserId,
    DateTime DateHeure,
    string? Type);
