namespace SoftTime.Application.Abstractions.Permissions;

public interface IParametersAccessGuard
{
    /// <summary>
    /// Consultation sensible : administrateur, ou porte 2 ET l'une des permissions fines, lues dans les droits effectifs
    /// (pas dans le jeton). Échec fermé si la lecture des droits échoue.
    /// </summary>
    Task EnsureCanReadAsync(string[] anyOf, CancellationToken cancellationToken = default);

    /// <summary>Opérations sur les autres comptes : réservées aux administrateurs (rôle 1) (rôle 1 uniquement).</summary>
    void RequireAdministrator();
}
