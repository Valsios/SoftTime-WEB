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

    /// <summary>
    /// Installation vierge ? Portage de <c>is-first-run</c> du desktop, avec un critère plus sain :
    /// le desktop cherchait un rôle nommé « administrateur », ici on constate simplement qu'aucun
    /// compte n'existe. Le critère ne dépend donc pas d'une convention de nommage des rôles, et il
    /// reste vrai après la suppression du dernier compte (seul moyen de sortir d'un verrouillage total).
    /// </summary>
    Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Crée le premier compte administrateur. Refusé dès qu'un compte existe, et le rôle
    /// <see cref="RoleAdministrateur"/> se voit garantir tous les droits : sans cela, l'application
    /// serait inaccessible juste après son installation.
    /// </summary>
    Task<AdminSetupResultDto> CreateAdminAsync(AdminSetupDto dto, CancellationToken cancellationToken = default);
}
