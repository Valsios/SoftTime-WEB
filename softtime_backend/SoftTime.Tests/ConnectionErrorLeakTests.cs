using AutoMapper;

using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Mapping;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>
/// K5/Q1-Q2 : une connexion qui échoue (hôte invalide, vrai SqlClient) ne doit révéler ni le mot de passe,
/// ni le login, ni la chaîne de connexion, ni dans le message renvoyé, ni dans ce que le journal « Unhandled » écrirait.
/// </summary>
public class ConnectionErrorLeakTests
{
    // Valeurs factices évidentes ; l'hôte n'existe pas (aucune base réelle n'est contactée).
    private const string Host = "127.0.0.1,1"; // port fermé de la boucle locale : refus immédiat, aucun serveur contacté
    private const string FakeLogin = "FAKE-LOGIN-NAME";
    private const string FakePassword = "FAKE-PWD-SECRET";

    private static readonly string[] Forbidden =
        { FakePassword, FakeLogin, "Password=", "User ID=", "Initial Catalog=", "Data Source=", "TrustServerCertificate" };

    private readonly InMemoryUnitOfWork _uow = new();

    private CatalogServices Catalog()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        var auth = new AuthServices(_uow, new Token(), mapper, new Mail(), new SoftTime.Tests.Http.AdminCurrentUser(), SoftTime.Tests.Http.TestRights.Service(_uow), new PasswordHasher<Responsable>());
        return new CatalogServices(_uow, mapper, null!, null!, new ExternalDiscoveryService(),
            new DatabaseConfigurationAuthorizer(new Admin(), auth), SoftTime.Tests.Http.TestRights.Service(_uow));
    }

    private static CancellationToken Short() => new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token;

    private static void AssertNoLeak(string where, string text)
    {
        foreach (var f in Forbidden)
            Assert.False(text.Contains(f, StringComparison.OrdinalIgnoreCase), $"{where} contient « {f} » : {text}");
    }

    // Les cinq échecs sont lancés en parallèle : chacun attend le délai de connexion (60 s) de la fabrique de chaînes.
    [Fact]
    public async Task Failed_connections_leak_neither_password_login_nor_connection_string()
    {
        var svc = Catalog();
        var ct = Short();

        async Task<(string Name, string Text)> Msg(string name, Func<Task<ConnectionTestResultDto>> act)
        {
            var r = await act();
            Assert.False(r.Success);
            Assert.StartsWith("Connexion impossible", r.Message);
            return (name, r.Message);
        }

        async Task<(string Name, string Text)> Thrown(string name, Func<Task> act)
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(act);
            // ILogger.LogError(ex, ...) écrit ex.ToString() : type, message, exceptions internes et pile d'appels.
            return (name + " (journal Unhandled, " + ex.GetType().Name + ")", ex.ToString());
        }

        var results = await Task.WhenAll(
            Msg("test sage", () => svc.Sage.TestSageConnectionAsync(new TestSageConnectionDto(Host, true, FakeLogin, FakePassword, "BASE", "STANDARD"), ct)),
            Msg("test pointeuse", () => svc.Pointeuse.TestPointeuseConnectionAsync(new TestPointeuseConnectionDto(Host, true, FakeLogin, FakePassword, "BASE", "STANDARD"), ct)),
            Thrown("découverte bases", () => svc.Discovery.DiscoverDatabasesAsync(new DiscoverDatabasesDto(Host, true, FakeLogin, FakePassword), ct)),
            Thrown("découverte tables", () => svc.Discovery.DiscoverTablesAsync(new DiscoverTablesDto(Host, "BASE", true, FakeLogin, FakePassword), ct)),
            Thrown("découverte colonnes", () => svc.Discovery.DiscoverColumnsAsync(new DiscoverColumnsDto(Host, "BASE", "T", true, FakeLogin, FakePassword), ct)));

        foreach (var (name, text) in results)
        {
            AssertNoLeak(name, text);
        }
    }

    private sealed class Admin : ICurrentUser
    {
        public decimal? UserId => 1;
        public string? Login => "a";
        public decimal? RoleId => 1;
        public string? Matricule => null;
        public IReadOnlyCollection<int> Rights => Array.Empty<int>();
        public bool HasRight(int droit) => true;
    }

    private sealed class Token : ITokenService
    {
        public string CreateToken(decimal userId, string login, decimal roleId, string? matricule, IEnumerable<int> rights) => "FAKE_TOKEN";
    }

    private sealed class Mail : IEmailService
    {
        public bool EstConfigure => true;
        public Task<bool> EnvoyerAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserCredentialsAsync(AccountMailRecipient destinataire, string? password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendUserUpdateAsync(AccountMailRecipient destinataire, string? newPassword, bool pseudoChanged, bool passwordChanged, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
