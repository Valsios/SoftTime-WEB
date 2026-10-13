using SoftTime.Domain.Models;

namespace SoftTime.Application.Abstractions;

/// <summary>
/// Envoi des messages aux utilisateurs. L'implémentation par défaut est un no-op tant que la
/// configuration SMTP est absente : créer un compte ne doit jamais échouer à cause de l'e-mail.
/// </summary>
public interface IEmailService
{
    /// <summary>La configuration SMTP permet-elle un envoi ?</summary>
    bool EstConfigure { get; }

    /// <summary>Envoie un message ; retourne <c>false</c> si l'envoi a échoué (jamais d'exception).</summary>
    Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>Identifiants d'un compte nouvellement créé.</summary>
    Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default);

    /// <summary>Changement d'identifiant ou de mot de passe (ne mentionne que ce qui a changé).</summary>
    Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default);
}
