using SoftTime.Domain.Models;

namespace SoftTime.Tests;

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
