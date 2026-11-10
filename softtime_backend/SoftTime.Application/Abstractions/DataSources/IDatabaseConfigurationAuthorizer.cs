using SoftTime.Application.Services;

namespace SoftTime.Application.Abstractions.DataSources;

/// <summary>
/// Autorise la configuration (ajout, modification, suppression, test, découverte) des bases SAGE et pointeuse.
/// Hors administrateur, exige la porte Bases ET la permission fine, que le compte soit personnalisé ou non.
/// </summary>
public interface IDatabaseConfigurationAuthorizer
{
    Task EnsureCanConfigureAsync(DatabaseKind kind, DatabaseConfigAction action, string? dbName, CancellationToken ct = default);

    /// <summary>Test de connexion et découverte : il faut pouvoir ajouter ou modifier au moins une base.</summary>
    Task EnsureCanConfigureAnyAsync(DatabaseKind? kind, CancellationToken ct = default);
}
