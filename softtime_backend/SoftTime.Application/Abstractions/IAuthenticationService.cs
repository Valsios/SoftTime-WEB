using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IAuthenticationService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rafraîchit la session : le jeton présenté est échangé contre un nouveau (rotation) et le jeton d'accès
    /// est réémis avec les droits effectifs actuels (un droit retiré disparaît au plus tard au rafraîchissement suivant).
    /// </summary>
    Task<LoginResponse> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Déconnexion : le jeton de rafraîchissement présenté ne peut plus servir.</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);

    Task<LoginResponse> GetMeAsync(decimal userId, CancellationToken cancellationToken = default);
}
