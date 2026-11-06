namespace SoftTime.Domain.Exceptions;

/// <summary>Appelant non authentifié ou identifiants refusés, traduit en HTTP 401.</summary>
public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message = "Non autorisé.")
        : base(message)
    {
    }
}
