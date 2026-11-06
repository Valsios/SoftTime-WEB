#nullable enable

namespace SoftTime.Application.ImportTemplates;

/// <summary>Colonne d'un modèle d'import, contrat de l'API d'aide.</summary>
public sealed record ImportTemplateColumnDto(
    string Header,
    bool Required,
    string Format,
    string Example,
    string? Note);
