namespace SoftTime.Domain.Models;

/// <summary>Message prêt à être remis au serveur SMTP.</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody);
