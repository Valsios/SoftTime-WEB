namespace SoftTime.Application.DTOs;

// RefreshToken : renvoyé à la connexion et au rafraîchissement seulement (null pour GET me).
public record LoginResponse(string Token, decimal UserId, string Login, string Nom, decimal RoleId, string? Matricule, IReadOnlyCollection<int> Rights, IReadOnlyCollection<SageDbDto> AuthorizedDatabases, IReadOnlyCollection<string>? Permissions = null, bool PermissionsCustomized = false, string? RefreshToken = null)
{
    public LoginResponse() : this(default!, default, default!, default!, default, default, default!, default!, null, false, null) { }
}
