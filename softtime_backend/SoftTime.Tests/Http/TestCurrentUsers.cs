using SoftTime.Application.Abstractions;

namespace SoftTime.Tests.Http;

/// <summary>Double de ICurrentUser : appelant HTTP simulé. L'administrateur est le rôle 1 uniquement.</summary>
public class TestCurrentUser : ICurrentUser
{
    public TestCurrentUser(decimal userId, decimal roleId, params int[] rights)
    {
        UserId = userId;
        RoleId = roleId;
        Rights = rights;
    }

    public decimal? UserId { get; }
    public string? Login => $"u{UserId}";
    public decimal? RoleId { get; }
    public string? Matricule => null;
    public IReadOnlyCollection<int> Rights { get; }
    public bool HasRight(int droit) => RoleId == 1 || Rights.Contains(droit);
}

/// <summary>
/// Appelant administrateur : les tests unitaires historiques de AuthService vérifient les règles internes
/// (rôle de l'appelant lu en base, protections des comptes administrateurs) derrière le contrôle d'entrée.
/// </summary>
public sealed class AdminCurrentUser : TestCurrentUser
{
    public AdminCurrentUser() : base(1, 1) { }
}
