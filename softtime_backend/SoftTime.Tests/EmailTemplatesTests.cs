using SoftTime.Application.Services;
using SoftTime.Domain.Models;

namespace SoftTime.Tests;

/// <summary>
/// Composition des messages envoyés aux utilisateurs (portage des gabarits de
/// <c>EmailService.GenerateEmailBody</c> / <c>GenerateUpdateEmailBody</c> du desktop).
/// </summary>
public class EmailTemplatesTests
{
    // ---- Échappement HTML ----

    [Fact]
    public void EchapperHtml_NeutralizesMarkupCharacters()
    {
        Assert.Equal("&lt;script&gt;", EmailTemplates.EchapperHtml("<script>"));
        Assert.Equal("A &amp; B", EmailTemplates.EchapperHtml("A & B"));
        Assert.Equal("&quot;", EmailTemplates.EchapperHtml("\""));
    }

    [Fact]
    public void EchapperHtml_NullBecomesEmpty()
        => Assert.Equal(string.Empty, EmailTemplates.EchapperHtml(null));

    [Fact]
    public void Identifiants_EscapesInjectedMarkup()
    {
        // Un nom saisi librement ne doit pas pouvoir casser le message ni y injecter de balise.
        var corps = EmailTemplates.Identifiants("<b>Nom</b>", "Ines", "login", "secret");
        Assert.DoesNotContain("<b>Nom</b>", corps);
        Assert.Contains("&lt;b&gt;Nom&lt;/b&gt;", corps);
    }

    // ---- Identifiants ----

    [Fact]
    public void Identifiants_ContainsLoginAndPassword()
    {
        var corps = EmailTemplates.Identifiants("Rakoto", "Ines", "irakoto", "MotDePasse!42");
        Assert.Contains("irakoto", corps);
        Assert.Contains("MotDePasse!42", corps);
        Assert.Contains("Ines", corps);
        Assert.Contains("Rakoto", corps);
    }

    [Fact]
    public void Identifiants_UsesTheCredentialsSubject()
        => Assert.Equal("Vos identifiants SoftTime", EmailTemplates.SujetIdentifiants);

    [Fact]
    public void Identifiants_ToleratesMissingValues()
    {
        var corps = EmailTemplates.Identifiants(null, null, null, null);
        Assert.Contains("Bonjour", corps);
        Assert.DoesNotContain("null", corps);
    }

    // ---- Mise à jour ----

    [Fact]
    public void MiseAJour_MentionsOnlyWhatChanged()
    {
        var corps = EmailTemplates.MiseAJour("Rakoto", "Ines", "nouveau.login", null, loginModifie: true, motDePasseModifie: false);
        Assert.Contains("nouveau.login", corps);
        Assert.DoesNotContain("Nouveau mot de passe", corps);
    }

    [Fact]
    public void MiseAJour_ShowsTheNewPasswordOnlyWhenItChanged()
    {
        var corps = EmailTemplates.MiseAJour("Rakoto", "Ines", "login", "NouveauMotDePasse!1", loginModifie: false, motDePasseModifie: true);
        Assert.Contains("NouveauMotDePasse!1", corps);
        Assert.DoesNotContain("Nouvel identifiant", corps);
    }

    [Fact]
    public void MiseAJour_UsesTheUpdateSubject()
        => Assert.Equal("Mise à jour de vos identifiants SoftTime", EmailTemplates.SujetMiseAJour);
}

/// <summary>Réglages SMTP : le service d'envoi doit rester inactif tant qu'ils sont incomplets.</summary>
public class EmailSettingsTests
{
    [Fact]
    public void EstConfigure_RequiresHostAndFromAddress()
    {
        Assert.False(new EmailSettings(null, 587, true, null, null, null, null).EstConfigure);
        Assert.False(new EmailSettings("smtp.exemple.com", 587, true, null, null, null, null).EstConfigure);
        Assert.False(new EmailSettings(null, 587, true, null, null, "no-reply@exemple.com", null).EstConfigure);
        Assert.True(new EmailSettings("smtp.exemple.com", 587, true, null, null, "no-reply@exemple.com", null).EstConfigure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void PortEffectif_FallsBackOnDefaultPort(int port)
        => Assert.Equal(EmailSettings.PortDefaut, new EmailSettings("smtp.exemple.com", port, true, null, null, "a@b.c", null).PortEffectif);

    [Fact]
    public void PortEffectif_KeepsAValidPort()
        => Assert.Equal(25, new EmailSettings("smtp.exemple.com", 25, false, null, null, "a@b.c", null).PortEffectif);

    [Fact]
    public void NomExpediteur_FallsBackOnTheAddress()
    {
        Assert.Equal("SoftTime", new EmailSettings("h", 587, true, null, null, "a@b.c", "SoftTime").NomExpediteur);
        Assert.Equal("a@b.c", new EmailSettings("h", 587, true, null, null, "a@b.c", null).NomExpediteur);
        // Un nom réduit à des espaces est traité comme absent.
        Assert.Equal("a@b.c", new EmailSettings("h", 587, true, null, null, "a@b.c", "   ").NomExpediteur);
    }
}
