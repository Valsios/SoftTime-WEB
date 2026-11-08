namespace SoftTime.Application.DTOs;

/// <summary>Champs de connexion communs aux formulaires de base externe (enregistrement et test).</summary>
public interface IExternalConnectionFields
{
    string? Serveur { get; }

    string? Login { get; }

    string? Password { get; }

    string? NomBd { get; }

    string TypeBase { get; }
}
