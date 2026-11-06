using System.Net;
using System.Net.Mail;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

namespace SoftTime.Infrastructure.Integrations;

/// <summary>
/// Envoi SMTP des identifiants utilisateurs, piloté par la section <c>Email</c> de la configuration
/// (<c>appsettings.json</c> ou variables d'environnement).
/// <para>
/// Le desktop utilisait MailKit ; ici on s'appuie sur <see cref="SmtpClient"/> de la bibliothèque
/// standard, qui évite une dépendance supplémentaire et suffit à un envoi TLS simple.
/// </para>
/// <para>
/// <b>Tant que la configuration est incomplète, aucun envoi n'est tenté</b> : le service est un no-op
/// qui journalise l'absence de configuration. Un envoi qui échoue est journalisé et retourné
/// <c>false</c>, il ne fait jamais échouer la création ou la modification du compte.
/// </para>
/// </summary>
public class EmailService : IEmailService
{
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptionsMonitor<EmailOptions> options, ILogger<EmailService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool EstConfigure => Reglages().EstConfigure;

    /// <inheritdoc />
    public async Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var reglages = Reglages();
        if (!reglages.EstConfigure)
        {
            _logger.LogWarning(
                "E-mail non envoyé : la section de configuration « Email » est incomplète (Host et FromAddress requis).");
            return false;
        }
        if (string.IsNullOrWhiteSpace(message.To))
        {
            _logger.LogWarning("E-mail non envoyé : aucun destinataire sur le compte.");
            return false;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(reglages.FromAddress!, reglages.NomExpediteur),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true
        };
        mail.To.Add(message.To.Trim());

        using var client = new SmtpClient(reglages.Host, reglages.PortEffectif)
        {
            EnableSsl = reglages.EnableSsl
        };
        if (!string.IsNullOrWhiteSpace(reglages.User))
            client.Credentials = new NetworkCredential(reglages.User, reglages.Password);

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            // Le compte est déjà enregistré : l'échec d'envoi est signalé, jamais propagé.
            _logger.LogError(ex, "Échec de l'envoi de l'e-mail à {Destinataire}.", message.To);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<bool> SendUserCredentialsAsync(string? email, string? pseudo, string? password, string? nom, string? prenom, CancellationToken cancellationToken = default)
        => EnvoyerAsync(
            new EmailMessage(email ?? string.Empty, EmailTemplates.SujetIdentifiants,
                EmailTemplates.Identifiants(nom, prenom, pseudo, password)),
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> SendUserUpdateAsync(string? email, string? pseudo, string? newPassword, string? nom, string? prenom, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default)
        => EnvoyerAsync(
            new EmailMessage(email ?? string.Empty, EmailTemplates.SujetMiseAJour,
                EmailTemplates.MiseAJour(nom, prenom, pseudo, newPassword, pseudoChanged, passwordChanged)),
            cancellationToken);

    /// <summary>Relu à chaque envoi : une modification de la configuration est prise en compte sans redémarrage.</summary>
    private EmailSettings Reglages() => _options.CurrentValue.ToSettings();
}
