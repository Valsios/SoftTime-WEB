using SoftTime.Domain.Models;

namespace SoftTime.Tests;

/// <summary>Réglages SMTP : le service d'envoi doit rester inactif tant qu'ils sont incomplets.</summary>
public class EmailSettingsTests
{
    [Fact]
    public void EstConfigure_RequiresHostAndFromAddress()
    {
        new EmailSettings(null, 587, true, null, null, null, null).EstConfigure.Should().BeFalse();
        new EmailSettings("smtp.exemple.com", 587, true, null, null, null, null).EstConfigure.Should().BeFalse();
        new EmailSettings(null, 587, true, null, null, "no-reply@exemple.com", null).EstConfigure.Should().BeFalse();
        new EmailSettings("smtp.exemple.com", 587, true, null, null, "no-reply@exemple.com", null).EstConfigure.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void PortEffectif_FallsBackOnDefaultPort(int port)
        => new EmailSettings("smtp.exemple.com", port, true, null, null, "a@b.c", null).PortEffectif.Should().Be(EmailSettings.PortDefaut);

    [Fact]
    public void PortEffectif_KeepsAValidPort()
        => new EmailSettings("smtp.exemple.com", 25, false, null, null, "a@b.c", null).PortEffectif.Should().Be(25);

    [Fact]
    public void NomExpediteur_FallsBackOnTheAddress()
    {
        new EmailSettings("h", 587, true, null, null, "a@b.c", "SoftTime").NomExpediteur.Should().Be("SoftTime");
        new EmailSettings("h", 587, true, null, null, "a@b.c", null).NomExpediteur.Should().Be("a@b.c");
        // Un nom réduit à des espaces est traité comme absent.
        new EmailSettings("h", 587, true, null, null, "a@b.c", "   ").NomExpediteur.Should().Be("a@b.c");
    }
}
