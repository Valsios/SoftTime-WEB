namespace SoftTime.Application.Abstractions.Authentication;

/// <summary>Titulaire d'un jeton d'accès : identifiant, login, rôle, matricule et portes accordées.</summary>
public sealed record TokenSubject(decimal UserId, string Login, decimal RoleId, string? Matricule, IEnumerable<int> Rights);
