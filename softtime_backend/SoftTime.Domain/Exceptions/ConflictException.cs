namespace SoftTime.Domain.Exceptions;

/// <summary>Doublon ou contrainte violée, traduit en HTTP 409.</summary>
public class ConflictException : DomainException
{
    public ConflictException(string message)
        : base(message)
    {
    }
}
