using Microsoft.AspNetCore.Identity;

using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class FirstRunService : IFirstRunService
{
    private readonly IUnitOfWork _uow;
    private readonly IEmailService _email;
    private readonly IPasswordHasher<Responsable> _passwordHasher;

    public FirstRunService(
        IUnitOfWork uow,
        IEmailService email,
        IPasswordHasher<Responsable>? passwordHasher = null)
    {
        _uow = uow;
        _email = email;
        _passwordHasher = passwordHasher ?? new PasswordHasher<Responsable>();
    }

    /// <summary>
    /// Installation vierge ? Portage de <c>is-first-run</c> du desktop, avec un critère plus sain :
    /// le desktop cherchait un rôle nommé « administrateur », ici on constate simplement qu'aucun
    /// compte n'existe. Le critère ne dépend donc pas d'une convention de nommage des rôles, et il
    /// reste vrai après la suppression du dernier compte (seul moyen de sortir d'un verrouillage total).
    /// </summary>
    public async Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default)
    {
        var repo = _uow.Repository<Responsable>();
        return await repo.CountAsync(repo.QueryReadOnly().Take(1), cancellationToken) == 0;
    }

    /// <summary>Verrou applicatif de la création initiale : deux appels simultanés passent l'un après l'autre.</summary>
    internal const string CreateAdminLockResource = "SoftTime.CreateAdmin";

    /// <summary>
    /// Crée le premier compte administrateur. Refusé dès qu'un compte existe, et le rôle
    /// <see cref="RoleAdministrateur"/> se voit garantir tous les droits : sans cela, l'application
    /// serait inaccessible juste après son installation.
    /// </summary>
    public async Task<AdminSetupResultDto> CreateAdminAsync(AdminSetupDto dto, CancellationToken cancellationToken = default)
    {
        await EnsureFirstRunAsync(cancellationToken);
        var login = (dto.Login ?? string.Empty).Trim();
        var motDePasse = dto.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dto.Nom))
            throw new DomainException("Le nom est requis.");
        if (string.IsNullOrWhiteSpace(login))
            throw new DomainException("L'identifiant est requis.");
        if (string.IsNullOrWhiteSpace(motDePasse))
            throw new DomainException("Le mot de passe est requis.");
        PasswordPolicy.EnsureValid(motDePasse);
        var admin = new Responsable
        {
            NomResponsable = dto.Nom.Trim(),
            Login = login,
            Password = _passwordHasher.HashPassword(null!, motDePasse),
            Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim()
        };

        // La vérification ci-dessus ne suffit pas : deux requêtes simultanées sur une base vide la passeraient
        // toutes les deux. Sous le verrou, la seconde attend la fin de la première, puis voit le compte créé.
        var entity = await _uow.RunExclusiveAsync(CreateAdminLockResource, ct => AddAdminAsync(admin, ct), cancellationToken);

        var envoye = await _email.SendUserCredentialsAsync(new AccountMailRecipient(entity.Email, entity.Login, entity.NomResponsable, null), motDePasse, cancellationToken);
        var message = envoye
            ? "Compte administrateur créé et identifiants envoyés par e-mail."
            : _email.EstConfigure
                ? "Compte administrateur créé. L'envoi de l'e-mail a échoué : communiquez les identifiants manuellement."
                : "Compte administrateur créé. Aucune configuration SMTP (« Email ») : communiquez les identifiants manuellement.";
        return new AdminSetupResultDto(entity.IdResponsable, entity.Login ?? login, envoye, message);
    }

    private async Task EnsureFirstRunAsync(CancellationToken ct)
    {
        if (!await IsFirstRunAsync(ct))
            throw new DomainException(
                "Des comptes existent déjà : la création initiale n'est plus possible. Utilisez l'écran Utilisateurs.");
    }

    /// <summary>Le premier compte reçoit toujours le rôle d'identifiant 1 : seul ce rôle est administrateur.</summary>
    private async Task<Responsable> AddAdminAsync(Responsable admin, CancellationToken ct)
    {
        await EnsureFirstRunAsync(ct);
        var role = await _uow.Repository<Role>().GetByIdAsync(AdminRules.AdminRoleId, ct)
            ?? throw new DomainException(
                "Le rôle Administrateur (identifiant 1) est introuvable : exécutez le script de création de la base.");
        admin.IdRole = role.IdRole;
        await _uow.Repository<Responsable>().AddAsync(admin, ct);
        await EnsureRoleHasAllRightsAsync(role.IdRole, ct);
        await _uow.SaveChangesAsync(ct);
        return admin;
    }

    /// <summary>Garantit que le rôle d'administration détient tous les droits (idempotent).</summary>
    private async Task EnsureRoleHasAllRightsAsync(decimal roleId, CancellationToken cancellationToken)
    {
        var droits = await _uow.Repository<DroitAcces>().ListAsync(_ => true, cancellationToken);
        var existants = await _uow.Repository<DroitRole>().ListAsync(p => p.IdRole == roleId, cancellationToken);
        var manquants = droits.Where(d => existants.All(e => e.IdDroit != d.IdDroit)).ToList();
        if (manquants.Count == 0)
            return;
        var repo = _uow.Repository<DroitRole>();
        foreach (var droit in manquants)
            await repo.AddAsync(new DroitRole { IdRole = roleId, IdDroit = droit.IdDroit }, cancellationToken);
    }
}
