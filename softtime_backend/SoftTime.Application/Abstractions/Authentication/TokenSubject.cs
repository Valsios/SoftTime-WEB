namespace SoftTime.Application.Abstractions.Authentication;

/// <summary>
/// Titulaire d'un jeton d'accès : identifiant, login, rôle, matricule et portes accordées, plus ses permissions
/// fines et l'empreinte de ses droits (un jeton dont l'empreinte n'est plus à jour est refusé).
/// </summary>
public sealed record TokenSubject(decimal UserId, string Login, decimal RoleId, string? Matricule, IEnumerable<int> Rights)
{
    public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();

    public string RightsStamp { get; init; } = string.Empty;
}
