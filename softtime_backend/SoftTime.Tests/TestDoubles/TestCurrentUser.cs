using SoftTime.Application.Abstractions;

namespace SoftTime.Tests;

/// <summary>Utilisateur connecté de test : le rôle 1 (administrateur) a tous les droits, les autres ceux donnés.</summary>
internal sealed class TestCurrentUser : ICurrentUser
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
