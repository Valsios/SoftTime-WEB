namespace SoftTime.Application.Abstractions;

/// <summary>Titulaire d'un compte destinataire d'un courriel : adresse, identifiant de connexion et nom.</summary>
public sealed record AccountMailRecipient(string? Email, string? Login, string? Nom, string? Prenom);
