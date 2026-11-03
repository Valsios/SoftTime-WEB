#nullable enable

namespace SoftTime.Domain.Exceptions;

/// <summary>Conflit avec l'état actuel : doublon, contrainte, confirmation requise (HTTP 409).</summary>
public class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }

    /// <summary>Corps de réponse propre au conflit (null : message seul), par exemple un aperçu à confirmer.</summary>
    public virtual object? Details => null;
}
