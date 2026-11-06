namespace SoftTime.Domain.Exceptions;

/// <summary>
/// Règle métier non respectée. Le message est écrit pour l'utilisateur, en français et sans détail technique :
/// le middleware global le renvoie tel quel avec le code HTTP 400.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
