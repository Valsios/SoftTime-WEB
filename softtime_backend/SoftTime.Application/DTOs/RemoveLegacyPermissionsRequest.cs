namespace SoftTime.Application.DTOs;

/// <summary>Anciens codes de base sans action à retirer d'un compte (confirmés par l'administrateur).</summary>
public record RemoveLegacyPermissionsRequest(IReadOnlyList<string> Codes)
{
    public RemoveLegacyPermissionsRequest() : this(Array.Empty<string>()) { }
}
