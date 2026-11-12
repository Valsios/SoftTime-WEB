namespace SoftTime.Application.Security;

/// <summary>
/// Un écran du catalogue : son code de page (« ressource.read », ou « ressource.manage » quand un seul droit couvre
/// tout l'écran) et les actions qu'il propose (« ressource.create », « ressource.update », « ressource.delete »).
/// </summary>
public sealed record PermissionItemDefinition(
    string Code,
    string Label,
    bool CanCreate = false,
    bool CanUpdate = false,
    bool CanDelete = false)
{
    /// <summary>Ressource de l'écran : le code de page sans son action (« categories » pour « categories.read »).</summary>
    public string Resource => Code[..Code.LastIndexOf('.')];

    /// <summary>Codes de l'écran, dans l'ordre d'affichage : page, puis création, modification, suppression.</summary>
    public IEnumerable<string> Codes()
    {
        yield return Code;
        if (CanCreate) yield return Resource + ".create";
        if (CanUpdate) yield return Resource + ".update";
        if (CanDelete) yield return Resource + ".delete";
    }
}
