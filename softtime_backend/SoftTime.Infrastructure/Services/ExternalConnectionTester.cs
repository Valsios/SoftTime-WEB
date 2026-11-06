using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Test de connexion commun aux bases SAGE et pointeuse : liste des tables, puis contrôle de la
/// structure standard attendue ou, pour une base « AUTRE », des tables et colonnes mappées.
/// </summary>
internal sealed class ExternalConnectionTester
{
    private readonly IExternalDiscoveryService _discovery;
    private readonly ISourceMappingValidator _validator;

    public ExternalConnectionTester(IExternalDiscoveryService discovery, ISourceMappingValidator validator)
    {
        _discovery = discovery;
        _validator = validator;
    }

    public async Task<ConnectionTestResultDto> TestAsync(ExternalConnectionTarget cible, StandardStructure standard, CancellationToken ct)
    {
        IReadOnlyList<string> tables;
        try
        {
            tables = await _discovery.ListTablesAsync(cible.Serveur, cible.NomBd, cible.SqlAuth, cible.Login, cible.Password, ct);
        }
        catch (ExternalConnectionException ex)
        {
            // Message lisible, sans texte SQL (voir ExternalDiscoveryService.Describe) ; toute autre erreur va au middleware (500 journalisé).
            return new ConnectionTestResultDto(false, $"Connexion impossible : {ex.Message}");
        }
        if (cible.TypeBase == "AUTRE")
            return await TesterMappingsAsync(cible, tables, ct);

        var missing = standard.Tables.Where(t => !tables.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        return missing.Count == 0
            ? new ConnectionTestResultDto(true, standard.Reconnue)
            : new ConnectionTestResultDto(false, standard.Manquante, missing);
    }

    private async Task<ConnectionTestResultDto> TesterMappingsAsync(
        ExternalConnectionTarget cible, IReadOnlyList<string> tables, CancellationToken ct)
    {
        var missingCustom = new List<string>();
        if (cible.Mappings is { Count: > 0 })
        {
            foreach (var t in cible.Mappings.Where(x => !string.IsNullOrWhiteSpace(x.SourceTable)).Select(x => x.SourceTable!))
            {
                if (!tables.Contains(t, StringComparer.OrdinalIgnoreCase) && !missingCustom.Contains(t, StringComparer.OrdinalIgnoreCase))
                    missingCustom.Add(t);
            }
            if (missingCustom.Count == 0)
            {
                var missingCols = await _validator.ValidateMappingColumnsAsync(
                    cible.Serveur, cible.NomBd, cible.SqlAuth, cible.Login, cible.Password, cible.Mappings, ct);
                if (missingCols.Count > 0)
                    return new ConnectionTestResultDto(false, "Connexion réussie mais certaines colonnes mappées sont introuvables.", missingCols);
            }
        }
        return missingCustom.Count == 0
            ? new ConnectionTestResultDto(true, "Connexion réussie.")
            : new ConnectionTestResultDto(false, "Connexion réussie mais certaines tables choisies sont introuvables.", missingCustom);
    }
}
