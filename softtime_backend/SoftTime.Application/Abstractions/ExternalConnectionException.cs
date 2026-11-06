namespace SoftTime.Application.Abstractions;

/// <summary>
/// Échec d'accès à une base externe (SAGE, pointeuse) : le message est une cause lisible et sans détail technique
/// (serveur injoignable, identifiants refusés…) ; l'erreur SQL d'origine reste dans InnerException pour le journal.
/// </summary>
public sealed class ExternalConnectionException : DomainException
{
    public ExternalConnectionException(string message, Exception innerException) : base(message, innerException) { }
}
