using Moq;

using SoftTime.Application.Abstractions.Authentication;
using SoftTime.Application.Abstractions.Notifications;
using SoftTime.Domain.Models;

namespace SoftTime.Tests;

/// <summary>Mocks Moq des services techniques sans effet observable dans les tests métier.</summary>
internal static class ServiceMocks
{
    /// <summary>Service d'e-mail configuré dont chaque envoi réussit.</summary>
    public static Mock<IEmailService> EmailService()
    {
        var mock = new Mock<IEmailService>();
        mock.SetupGet(s => s.EstConfigure).Returns(true);
        mock.Setup(s => s.EnvoyerAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mock.Setup(s => s.SendUserCredentialsAsync(It.IsAny<AccountMailRecipient>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mock.Setup(s => s.SendUserUpdateAsync(
                It.IsAny<AccountMailRecipient>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock;
    }

    /// <summary>Émetteur de jetons déterministe : <c>FAKE_JWT_TOKEN_{UserId}_{Login}</c>.</summary>
    public static Mock<ITokenService> TokenService()
    {
        var mock = new Mock<ITokenService>();
        mock.Setup(s => s.CreateToken(It.IsAny<TokenSubject>()))
            .Returns((TokenSubject subject) => $"FAKE_JWT_TOKEN_{subject.UserId}_{subject.Login}");
        return mock;
    }
}
