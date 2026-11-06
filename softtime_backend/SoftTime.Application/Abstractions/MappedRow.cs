namespace SoftTime.Application.Abstractions;

/// <summary>Ligne projetée depuis une source externe : valeur par code de rôle (FieldRole.Code).</summary>
public sealed record MappedRow(IReadOnlyDictionary<string, string?> Values)
{
    public string? this[string role] => Values.TryGetValue(role, out var v) ? v : null;
}
