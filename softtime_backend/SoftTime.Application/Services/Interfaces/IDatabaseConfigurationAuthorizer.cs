using SoftTime.Application.Abstractions;
using SoftTime.Domain;
using SoftTime.Application.Services;

namespace SoftTime.Application.Services.Interfaces;

/// <summary>Contrat de <see cref="DatabaseConfigurationAuthorizer"/> (convention §3.5 : les consommateurs dépendent de l'abstraction).</summary>
public interface IDatabaseConfigurationAuthorizer
{
    Task EnsureCanConfigureAsync(DatabaseKind kind, DatabaseConfigAction action, string? dbName, CancellationToken ct = default);

    /// <summary>Test de connexion et découverte : il faut pouvoir ajouter ou modifier au moins une base.</summary>
    Task EnsureCanConfigureAnyAsync(DatabaseKind? kind, CancellationToken ct = default);
}
