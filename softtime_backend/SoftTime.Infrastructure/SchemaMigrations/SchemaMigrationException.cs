namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Échec d'une migration. Le message est en français et ne contient ni secret ni chaîne de connexion.</summary>
public sealed class SchemaMigrationException : Exception
{
    public SchemaMigrationException(string message, Exception? inner = null) : base(message, inner) { }
}
