namespace SoftTime.Api.Security;

/// <summary>Politique de limitation des endpoints d'authentification exposés sans jeton (voir Program.cs).</summary>
public static class AuthRateLimit
{
    public const string Policy = "auth";
}
