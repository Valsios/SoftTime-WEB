using Microsoft.AspNetCore.Identity;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Security;

/// <summary>
/// Hachage des mots de passe avec BCrypt (convention §9.1). Les hachages ASP.NET Identity (PBKDF2) déjà stockés
/// restent acceptés : leur vérification réussie renvoie <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>,
/// et la connexion les remplace aussitôt par un hachage BCrypt.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher<T_RESPONSABLE>
{
    public const int WorkFactor = 12;

    private readonly PasswordHasher<T_RESPONSABLE> _identityHasher = new();

    public string HashPassword(T_RESPONSABLE user, string password)
        => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public PasswordVerificationResult VerifyHashedPassword(T_RESPONSABLE user, string hashedPassword, string providedPassword)
    {
        if (!IsBcrypt(hashedPassword))
        {
            // Ancien format : une FormatException (valeur en clair) remonte, comme avec le hacheur Identity.
            var legacy = _identityHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
            return legacy == PasswordVerificationResult.Failed
                ? PasswordVerificationResult.Failed
                : PasswordVerificationResult.SuccessRehashNeeded;
        }
        try
        {
            if (!BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword))
                return PasswordVerificationResult.Failed;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return PasswordVerificationResult.Failed;
        }
        return CostOf(hashedPassword) < WorkFactor
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    public static bool IsBcrypt(string? hashedPassword)
        => hashedPassword is { Length: 60 }
           && (hashedPassword.StartsWith("$2a$", StringComparison.Ordinal)
               || hashedPassword.StartsWith("$2b$", StringComparison.Ordinal)
               || hashedPassword.StartsWith("$2y$", StringComparison.Ordinal));

    private static int CostOf(string hashedPassword)
        => int.TryParse(hashedPassword.AsSpan(4, 2), out var cost) ? cost : 0;
}
