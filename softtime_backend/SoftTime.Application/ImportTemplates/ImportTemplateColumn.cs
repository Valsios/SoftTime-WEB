#nullable enable

namespace SoftTime.Application.ImportTemplates;

/// <summary>Colonne attendue par un import, telle qu'exposée dans l'aide « Modèles d'import ».</summary>
public sealed record ImportTemplateColumn(
    string Header,
    bool Required,
    string Format,
    string Example,
    string? Note = null);
