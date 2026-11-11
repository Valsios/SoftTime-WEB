using SoftTime.Application.Security;
using SoftTime.Application.Services;

namespace SoftTime.Application.Abstractions.DataSources;

/// <summary>
/// Autorise la configuration (ajout, modification, suppression, test, découverte) des bases SAGE et pointeuse.
/// Hors administrateur, exige la porte Bases ET la permission fine, que le compte soit personnalisé ou non.
/// </summary>
public interface IDatabaseConfigurationAuthorizer
{
    /// <summary>Ajouter (cible null), modifier ou supprimer une base : la cible est désignée par son identifiant.</summary>
    Task EnsureCanConfigureAsync(DatabaseKind kind, DatabaseConfigAction action, DatabaseRef? target, CancellationToken ct = default);

    /// <summary>Test de connexion et découverte : il faut pouvoir ajouter ou modifier au moins une base.</summary>
    Task EnsureCanConfigureAnyAsync(DatabaseKind? kind, CancellationToken ct = default);
}
