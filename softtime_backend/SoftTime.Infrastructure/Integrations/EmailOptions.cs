namespace SoftTime.Infrastructure.Integrations;

/// <summary>Section « Email » de la configuration : serveur SMTP et expéditeur des e-mails aux utilisateurs.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string? Host { get; set; }

    public int Port { get; set; } = EmailSettings.PortDefaut;

    public bool EnableSsl { get; set; } = true;

    public string? User { get; set; }

    public string? Password { get; set; }

    public string? FromAddress { get; set; }

    public string? FromName { get; set; }

    /// <summary>Réglages effectifs passés au Domain.</summary>
    public EmailSettings ToSettings() => new(Host, Port, EnableSsl, User, Password, FromAddress, FromName);
}
