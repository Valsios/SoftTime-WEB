namespace SoftTime.Application.Security;

/// <summary>
/// Distingue une erreur métier levée par le code SoftTime, dont le message est écrit pour l'utilisateur, d'une erreur
/// technique levée par une bibliothèque (EF Core, SqlClient, .NET), dont le message peut révéler le schéma, le serveur,
/// un chemin ou un nom de compte. Seul le message d'une erreur métier peut être renvoyé au client.
/// </summary>
public static class ExceptionOrigin
{
    private const string ApplicationNamespace = "SoftTime.";

    /// <summary>
    /// Vrai si l'exception a été levée par une méthode du code SoftTime. Pour une méthode async, la méthode qui lève
    /// est le <c>MoveNext</c> de sa machine à états, type imbriqué dont l'espace de noms est celui de la classe.
    /// </summary>
    public static bool IsApplication(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var ns = exception.TargetSite?.DeclaringType?.Namespace;
        return ns is not null && ns.StartsWith(ApplicationNamespace, StringComparison.Ordinal);
    }
}
