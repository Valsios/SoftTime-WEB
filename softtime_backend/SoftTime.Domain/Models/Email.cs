namespace SoftTime.Domain.Models;

/// <summary>
/// Réglages SMTP, lus dans la section <c>Email</c> de la configuration.
/// <see cref="EstConfigure"/> vaut <c>false</c> tant que l'hôte et l'expéditeur ne sont pas renseignés :
/// dans ce cas l'envoi est ignoré (les comptes restent créés, sans e-mail).
/// </summary>
public sealed record EmailSettings(
    string? Host,
    int Port,
    bool EnableSsl,
    string? User,
    string? Password,
    string? FromAddress,
    string? FromName)
{
    /// <summary>Port SMTP par défaut quand la configuration ne le précise pas.</summary>
    public const int PortDefaut = 587;

    public bool EstConfigure => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    /// <summary>Nom affiché de l'expéditeur, à défaut l'adresse.</summary>
    public string NomExpediteur =>
        string.IsNullOrWhiteSpace(FromName) ? FromAddress ?? string.Empty : FromName;

    /// <summary>Port effectif : <see cref="Port"/> s'il est valide, sinon <see cref="PortDefaut"/>.</summary>
    public int PortEffectif => Port is > 0 and <= 65535 ? Port : PortDefaut;
}

/// <summary>Message prêt à être remis au serveur SMTP.</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody);
