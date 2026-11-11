using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.SelfService;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.SelfService;

/// <summary>
/// Garde dédiée de « Mon espace ». Elle ne passe PAS par <c>ITenantConnectionService.EnsureAuthorizedAsync</c>
/// (qui exige la porte 1) :
/// <list type="number">
///   <item>permission fine <c>self.read</c> effective (aucune porte n'est requise) ;</item>
///   <item>matricule du jeton (<see cref="ICurrentUser.Matricule"/>), non vide, jamais pris dans la requête ; il doit
///   être égal au matricule COURANT du compte, relu à chaque appel (compte supprimé, matricule retiré ou changé :
///   refus) ; si un AUTRE compte porte la même clé de matricule, aucun des porteurs n'accède à Mon espace ;</item>
///   <item>le compte a exactement UNE base autorisée ; une base courante envoyée par le client doit être
///   celle-là ;</item>
///   <item>le matricule existe dans les cartes de paie de cette base.</item>
/// </list>
/// Tout refus est une <see cref="ForbiddenException"/> (403) au message explicite.
/// </summary>
public sealed class SelfServiceGuard : ISelfServiceGuard
{
    private const string AccesRefuse = "Accès refusé.";
    private const string SansMatricule = "Aucun matricule n'est associé à ce compte : contactez la RH.";

    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly ICompanyContext _company;
    private readonly IEffectiveRightsService _rights;

    public SelfServiceGuard(IUnitOfWork uow, ICurrentUser user, ICompanyContext company, IEffectiveRightsService rights)
    {
        _uow = uow;
        _user = user;
        _company = company;
        _rights = rights;
    }

    public async Task<SelfContext> ResolveAsync(CancellationToken ct = default)
    {
        if (_user.UserId is not decimal uid)
            throw new ForbiddenException(AccesRefuse);
        await VerifierPermissionAsync(uid, ct);

        var matricule = (_user.Matricule ?? string.Empty).Trim();
        if (matricule.Length == 0)
            throw new ForbiddenException(SansMatricule);
        matricule = await MatriculeCourantAsync(uid, matricule, ct);

        var sage = await BaseAutoriseeAsync(uid, ct);
        var cle = MatriculeIdentity.Cle(matricule);
        var cartes = (await _uow.Repository<CardPaie>().ListReadOnlyAsync(c => c.SageBdd == sage, ct))
            .Where(c => !string.IsNullOrWhiteSpace(c.SageMatricule) && MatriculeIdentity.Cle(c.SageMatricule) == cle)
            .ToList();
        if (cartes.Count == 0)
            throw new ForbiddenException("Ce matricule est inconnu dans la base : contactez la RH.");
        return new SelfContext(uid, sage, matricule, cle, cartes);
    }

    /// <summary>Permission explicite : l'accès « administrateur à tout » ne remplace pas self.read.</summary>
    private async Task VerifierPermissionAsync(decimal uid, CancellationToken ct)
    {
        EffectiveRights effective;
        try
        {
            effective = await _rights.GetAsync(uid, ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Échec fermé : des droits illisibles ne donnent jamais accès.
            _rights.LogReadFailure(uid, ex);
            throw new ForbiddenException(AccesRefuse);
        }
        if (!effective.Permissions.Contains(PermissionCatalog.SelfRead))
            throw new ForbiddenException("Ce compte n'a pas accès à Mon espace.");
    }

    /// <summary>
    /// Le compte est relu à CHAQUE appel (sans cache) : le jeton ne peut pas garder un matricule que la RH a retiré,
    /// changé ou supprimé entre-temps ; le matricule de la base fait foi. La même lecture ramène les comptes dont le
    /// matricule contient celui du jeton (sur-ensemble SQL) pour vérifier l'unicité : un matricule = un seul compte.
    /// Si un autre compte porte la même clé, AUCUN des porteurs ne lit, et le message ne désigne jamais l'autre compte.
    /// </summary>
    private async Task<string> MatriculeCourantAsync(decimal uid, string matricule, CancellationToken ct)
    {
        var noyau = MatriculeIdentity.Cle(matricule);
        var candidats = await _uow.Repository<Responsable>().ListReadOnlyAsync(
            r => r.IdResponsable == uid || (r.Matricule != null && r.Matricule.ToUpper().Contains(noyau)), ct);
        var compte = candidats.FirstOrDefault(r => r.IdResponsable == uid)
            ?? throw new ForbiddenException("Ce compte n'existe plus : contactez la RH.");
        var courant = (compte.Matricule ?? string.Empty).Trim();
        if (courant.Length == 0)
            throw new ForbiddenException(SansMatricule);
        var cleCourante = MatriculeIdentity.Cle(courant);
        if (cleCourante != MatriculeIdentity.Cle(matricule))
            throw new ForbiddenException("Le matricule de ce compte a changé depuis la connexion : reconnectez-vous.");
        if (candidats.Any(r => r.IdResponsable != uid && MatriculeIdentity.Cle(r.Matricule) == cleCourante))
            throw new ForbiddenException("Ce matricule est porté par plusieurs comptes : contactez la RH.");
        return courant;
    }

    /// <summary>
    /// Unique base autorisée du compte. La base courante vient d'un en-tête que le client peut modifier : elle ne peut
    /// que confirmer la base autorisée ; absente, la base autorisée devient la base courante de la requête.
    /// </summary>
    private async Task<string> BaseAutoriseeAsync(decimal uid, CancellationToken ct)
    {
        var idsBases = (await _uow.Repository<BddAutorisee>().ListReadOnlyAsync(a => a.IdResponsable == uid, ct))
            .Select(a => a.IdBdd).Distinct().ToList();
        if (idsBases.Count != 1)
            throw new ForbiddenException("Ce compte doit avoir exactement une base autorisée : contactez la RH.");
        var idBase = idsBases[0];
        var sage = (await _uow.Repository<BddSage>().ListReadOnlyAsync(s => s.Id == idBase, ct))
            .FirstOrDefault()?.NomBd?.Trim();
        if (string.IsNullOrEmpty(sage))
            throw new ForbiddenException("La base autorisée de ce compte est introuvable : contactez la RH.");

        var courante = _company.SageDatabase?.Trim();
        if (!string.IsNullOrEmpty(courante) && !string.Equals(courante, sage, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Base non autorisée pour ce compte.");
        if (string.IsNullOrEmpty(courante))
            _company.Set(sage, _company.PointeuseDatabase);
        return sage;
    }
}
