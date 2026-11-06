using Microsoft.Extensions.Options;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.SelfService;

/// <summary>
/// Réglage de « Mon espace » : désactivé par défaut. Section « SelfService », clé « Enabled »
/// (variable d'environnement SelfService__Enabled). Désactivé, toutes les routes répondent 404.
/// </summary>
public sealed class SelfServiceOptions
{
    public const string Section = "SelfService";

    public bool Enabled { get; set; }
}

/// <summary>Identité résolue d'un salarié : tout vient du jeton et de la base, jamais de la requête.</summary>
public sealed record SelfContext(decimal UserId, string SageDb, string Matricule, string Cle, IReadOnlyList<T_CARDPAIE> Cartes);

/// <summary>
/// Garde dédiée de « Mon espace ». Elle ne passe PAS par TenantConnectionService.EnsureAuthorizedAsync (qui exige la porte 1) :
/// <list type="number">
///   <item>permission fine <c>SELF:READ</c> effective (aucune porte n'est requise) ;</item>
///   <item>matricule lu UNIQUEMENT dans ICurrentUser.Matricule (claim du jeton), non vide ;</item>
///   <item>le compte a exactement UNE base autorisée dans T_BDD_AUTORISE ; une base courante envoyée par le client doit être celle-là ;</item>
///   <item>le matricule existe dans T_CARDPAIE de cette base.</item>
/// </list>
/// Tout refus est une ForbiddenOperationException (403) au message explicite.
/// </summary>
public class SelfServiceGuard
{
    public const string Permission = "SELF:READ";

    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly ICompanyContext _company;
    private readonly EffectiveRightsService _rights;
    private readonly SelfServiceOptions _options;

    public SelfServiceGuard(IUnitOfWork uow, ICurrentUser user, ICompanyContext company, EffectiveRightsService rights, SelfServiceOptions options)
    {
        _uow = uow;
        _user = user;
        _company = company;
        _rights = rights;
        _options = options;
    }

    public bool Enabled => _options.Enabled;

    public virtual async Task<SelfContext> ResolveAsync(CancellationToken ct = default)
    {
        if (_user.UserId is not decimal uid)
            throw new ForbiddenOperationException("Accès refusé.");

        EffectiveRights effective;
        try
        {
            effective = await _rights.GetAsync(uid, ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Échec fermé : des droits illisibles ne donnent jamais accès.
            _rights.LogReadFailure(uid, ex);
            throw new ForbiddenOperationException("Accès refusé.");
        }
        // Permission explicite : l'accès « administrateur à tout » ne remplace pas SELF:READ.
        if (!effective.Permissions.Contains(Permission))
            throw new ForbiddenOperationException("Ce compte n'a pas accès à Mon espace.");

        var matricule = (_user.Matricule ?? string.Empty).Trim();
        if (matricule.Length == 0)
            throw new ForbiddenOperationException("Aucun matricule n'est associé à ce compte : contactez la RH.");

        var acces = await _uow.Repository<T_BDD_AUTORISE>().ListAsync(a => a.IDRESPONSABLE == uid, ct);
        var idsBases = acces.Select(a => a.IDBDD).Distinct().ToList();
        if (idsBases.Count != 1)
            throw new ForbiddenOperationException("Ce compte doit avoir exactement une base autorisée : contactez la RH.");
        var idBase = idsBases[0];
        var base_ = (await _uow.Repository<T_BDD_SAGE>().ListAsync(s => s.ID == idBase, ct)).FirstOrDefault();
        var sage = base_?.NOM_BD?.Trim();
        if (string.IsNullOrEmpty(sage))
            throw new ForbiddenOperationException("La base autorisée de ce compte est introuvable : contactez la RH.");

        // La base courante vient d'un en-tête que le client peut modifier : elle ne peut que confirmer la base autorisée.
        var courante = _company.SageDatabase?.Trim();
        if (!string.IsNullOrEmpty(courante) && !string.Equals(courante, sage, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenOperationException("Base non autorisée pour ce compte.");
        if (string.IsNullOrEmpty(courante))
            _company.Set(sage, _company.PointeuseDatabase);

        var cle = MatriculeIdentity.Cle(matricule);
        var cartes = (await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sage, ct))
            .Where(c => !string.IsNullOrWhiteSpace(c.SAGE_MATRICULE) && MatriculeIdentity.Cle(c.SAGE_MATRICULE) == cle)
            .ToList();
        if (cartes.Count == 0)
            throw new ForbiddenOperationException("Ce matricule est inconnu dans la base : contactez la RH.");

        return new SelfContext(uid, sage, matricule, cle, cartes);
    }
}
