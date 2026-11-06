namespace SoftTime.Domain.Exceptions;

/// <summary>Appelant authentifié mais sans le droit requis, traduit en HTTP 403.</summary>
public class ForbiddenException : DomainException
{
    public ForbiddenException(string message = "Accès interdit.")
        : base(message)
    {
    }
}
