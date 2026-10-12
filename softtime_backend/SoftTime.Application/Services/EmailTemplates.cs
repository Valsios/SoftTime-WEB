namespace SoftTime.Application.Services;

/// <summary>
/// Composition des messages envoyés aux utilisateurs.
/// Classe pure : le rendu HTML est ainsi testable sans SMTP, et tout ce qui vient de la base
/// (nom, prénom, login) est échappé — un caractère « &lt; » dans un nom casserait sinon le message.
/// </summary>
public static class EmailTemplates
{
    /// <summary>Sujet du message d'accueil d'un nouveau compte.</summary>
    public const string SujetIdentifiants = "Vos identifiants SoftTime";

    /// <summary>Sujet du message de modification d'un compte.</summary>
    public const string SujetMiseAJour = "Mise à jour de vos identifiants SoftTime";

    /// <summary>Échappe une valeur destinée à être insérée dans du HTML.</summary>
    public static string EchapperHtml(string? valeur)
        => (valeur ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    /// <summary>Message d'accueil : rappelle les identifiants transmis par l'administrateur.</summary>
    public static string Identifiants(string? nom, string? prenom, string? login, string? motDePasse)
        => $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:Arial,sans-serif;line-height:1.6;color:#333">
          <div style="max-width:600px;margin:0 auto;padding:20px">
            <h1 style="font-size:20px">SoftTime — Votre compte a été créé</h1>
            <p>Bonjour {EchapperHtml(prenom)} {EchapperHtml(nom)},</p>
            <p>Un compte vous a été créé. Voici vos identifiants de connexion :</p>
            <ul style="background:#f8fafc;padding:18px;border-left:4px solid #2563eb">
              <li><strong>Identifiant :</strong> {EchapperHtml(login)}</li>
              <li><strong>Mot de passe :</strong> {EchapperHtml(motDePasse)}</li>
            </ul>
            <p style="background:#fef3c7;padding:12px">
              Changez ce mot de passe après votre première connexion.
            </p>
          </div>
        </body>
        </html>
        """;

    /// <summary>
    /// Message de modification : ne mentionne que ce qui a réellement changé. Le mot de passe
    /// n'apparaît que s'il a été remplacé.
    /// </summary>
    public static string MiseAJour(
        string? nom, string? prenom, string? login, string? nouveauMotDePasse, bool loginModifie, bool motDePasseModifie)
    {
        var changements = string.Empty;
        if (loginModifie)
            changements += $"<li><strong>Nouvel identifiant :</strong> {EchapperHtml(login)}</li>";
        if (motDePasseModifie)
            changements += $"<li><strong>Nouveau mot de passe :</strong> {EchapperHtml(nouveauMotDePasse)}</li>";
        return $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:Arial,sans-serif;line-height:1.6;color:#333">
          <div style="max-width:600px;margin:0 auto;padding:20px">
            <h1 style="font-size:20px">SoftTime — Vos identifiants ont été mis à jour</h1>
            <p>Bonjour {EchapperHtml(prenom)} {EchapperHtml(nom)},</p>
            <p>Les informations de votre compte ont changé :</p>
            <ul style="background:#f8fafc;padding:18px;border-left:4px solid #f59e0b">
              {changements}
            </ul>
            <p style="background:#fef3c7;padding:12px">
              Si vous n'êtes pas à l'origine de ce changement, contactez votre administrateur.
            </p>
          </div>
        </body>
        </html>
        """;
    }
}
