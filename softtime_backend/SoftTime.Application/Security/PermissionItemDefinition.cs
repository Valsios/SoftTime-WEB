namespace SoftTime.Application.Security;

/// <summary>
/// Un écran du catalogue : son code de page (lecture) et les actions qu'il propose (code de page suivi de
/// « :CREATE », « :UPDATE » ou « :DELETE »).
/// </summary>
public sealed record PermissionItemDefinition(
    string Code,
    string Label,
    bool CanCreate = false,
    bool CanUpdate = false,
    bool CanDelete = false)
{
    /// <summary>Codes de l'écran, dans l'ordre d'affichage : page, puis création, modification, suppression.</summary>
    public IEnumerable<string> Codes()
    {
        yield return Code;
        if (CanCreate) yield return Code + ":CREATE";
        if (CanUpdate) yield return Code + ":UPDATE";
        if (CanDelete) yield return Code + ":DELETE";
    }
}
