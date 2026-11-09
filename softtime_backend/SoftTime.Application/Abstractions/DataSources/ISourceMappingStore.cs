using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.DataSources;

public interface ISourceMappingStore
{
    Task RemoveMappingsAsync(bool isSage, int connectionId, CancellationToken ct);

    /// <summary>
    /// Mot de passe à utiliser pour tester ou explorer une connexion. Un mot de passe saisi l'emporte.
    /// Sinon, pour une base existante, le mot de passe stocké (jamais renvoyé au client) n'est utilisé que si
    /// l'appelant peut modifier cette base ET que serveur, login et mode d'authentification sont inchangés :
    /// le mot de passe stocké ne doit jamais être envoyé vers un autre serveur choisi par le client.
    /// </summary>
    Task<string?> ResolveStoredPasswordAsync(ConnectionAttempt saisie, CancellationToken ct);

    Task ReplaceMappingsAsync(bool isSage, int connectionId, IReadOnlyList<SourceEntityMappingDto> mappings, CancellationToken ct);

    Task<IReadOnlyList<SourceEntityMappingDto>> LoadMappingsForAsync(bool isSage, int connectionId, CancellationToken ct);

    Task<Dictionary<int, List<SourceEntityMappingDto>>> LoadMappingLookupAsync(bool isSage, CancellationToken ct);

    /// <summary>
    /// Variante ciblée : ne charge que les mappings des connexions de la page courante, pour éviter
    /// de relire toute la table à chaque page. Les champs sont limités aux mappings sélectionnés.
    /// </summary>
    Task<Dictionary<int, List<SourceEntityMappingDto>>> LoadMappingLookupAsync(bool isSage, IReadOnlyCollection<int> connectionIds, CancellationToken ct);
}
