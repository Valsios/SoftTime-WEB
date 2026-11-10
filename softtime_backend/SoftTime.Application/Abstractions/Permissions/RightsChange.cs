namespace SoftTime.Application.Abstractions.Permissions;

/// <summary>Modification de droits à journaliser : opérateur, cible, action et permissions avant / après.</summary>
public sealed record RightsChange(
    decimal IdOperateur,
    string LoginOperateur,
    string TypeCible,
    decimal IdCible,
    string? NomCible,
    string Action,
    IEnumerable<string>? PermsAvant,
    IEnumerable<string>? PermsApres);
