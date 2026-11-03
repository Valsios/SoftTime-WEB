namespace SoftTime.Domain.Exceptions;

/// <summary>
/// Erreur métier prévisible dont le message est destiné à l'utilisateur (HTTP 400). Racine de la hiérarchie :
/// le middleware global traduit chaque sous-type en code HTTP ; toute autre exception est un bug (500, message masqué).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
