namespace SoftTime.Domain.Exceptions;

/// <summary>Appelant non authentifié ou session invalide (HTTP 401 : le client se reconnecte).</summary>
public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message = "Non autorisé.") : base(message) { }
}
