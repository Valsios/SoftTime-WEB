namespace SoftTime.Application.DTOs;

/// <summary>
/// Demande de checklist d'avancement : même période (mois civil précédent sans dates) et même département que
/// l'analyse.
/// <see cref="ReuseCached"/> (faux par défaut) autorise la dernière checklist conservée pendant le délai du cache.
/// <see cref="Perimetre"/> identifie l'utilisateur : il entre dans la clé du cache, jamais dans le résultat.
/// </summary>
public sealed record ClosingChecklistRequest(
    DateTime From,
    DateTime To,
    string? Departement = null,
    bool ReuseCached = false,
    string Perimetre = "");
