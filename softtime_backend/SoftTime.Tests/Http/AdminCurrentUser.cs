namespace SoftTime.Tests.Http;

/// <summary>
/// Appelant administrateur : les tests unitaires historiques de AuthService vérifient les règles internes
/// (rôle de l'appelant lu en base, protections des comptes administrateurs) derrière le contrôle d'entrée.
/// </summary>
public sealed class AdminCurrentUser : TestCurrentUser
{
    public AdminCurrentUser() : base(1, 1) { }
}
