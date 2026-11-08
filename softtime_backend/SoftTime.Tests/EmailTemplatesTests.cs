using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;

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
        // Act & Assert
        EmailTemplates.EchapperHtml("<script>").Should().Be("&lt;script&gt;");
        EmailTemplates.EchapperHtml("A & B").Should().Be("A &amp; B");
        EmailTemplates.EchapperHtml("\"").Should().Be("&quot;");
    }

    [Fact]
    public void EchapperHtml_NullBecomesEmpty()
    {
        // Act
        var result = EmailTemplates.EchapperHtml(null);

        // Assert
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void Identifiants_EscapesInjectedMarkup()
    {
        // Act
        // Un nom saisi librement ne doit pas pouvoir casser le message ni y injecter de balise.
        var corps = EmailTemplates.Identifiants("<b>Nom</b>", "Ines", "login", "secret");

        // Assert
        corps.Should().NotContain("<b>Nom</b>");
        corps.Should().Contain("&lt;b&gt;Nom&lt;/b&gt;");
    }

    // ---- Identifiants ----

    [Fact]
    public void Identifiants_ContainsLoginAndPassword()
    {
        // Act
        var corps = EmailTemplates.Identifiants("Rakoto", "Ines", "irakoto", "MotDePasse!42");

        // Assert
        corps.Should().Contain("irakoto");
        corps.Should().Contain("MotDePasse!42");
        corps.Should().Contain("Ines");
        corps.Should().Contain("Rakoto");
    }

    [Fact]
    public void Identifiants_UsesTheCredentialsSubject()
        => EmailTemplates.SujetIdentifiants.Should().Be("Vos identifiants SoftTime");

    [Fact]
    public void Identifiants_ToleratesMissingValues()
    {
        // Act
        var corps = EmailTemplates.Identifiants(null, null, null, null);

        // Assert
        corps.Should().Contain("Bonjour");
        corps.Should().NotContain("null");
    }

    // ---- Mise à jour ----

    [Fact]
    public void MiseAJour_MentionsOnlyWhatChanged()
    {
        // Act
        var corps = EmailTemplates.MiseAJour(new AccountMailRecipient(null, "nouveau.login", "Rakoto", "Ines"), null, true, false);

        // Assert
        corps.Should().Contain("nouveau.login");
        corps.Should().NotContain("Nouveau mot de passe");
    }

    [Fact]
    public void MiseAJour_ShowsTheNewPasswordOnlyWhenItChanged()
    {
        // Act
        var corps = EmailTemplates.MiseAJour(new AccountMailRecipient(null, "login", "Rakoto", "Ines"), "NouveauMotDePasse!1", false, true);

        // Assert
        corps.Should().Contain("NouveauMotDePasse!1");
        corps.Should().NotContain("Nouvel identifiant");
    }

    [Fact]
    public void MiseAJour_UsesTheUpdateSubject()
        => EmailTemplates.SujetMiseAJour.Should().Be("Mise à jour de vos identifiants SoftTime");
}
