using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Security;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

public class TenantConnectionService : ITenantConnectionService
{
    public const string AccessDenied = "Vous n'avez pas accès à cette base.";

    private readonly IUnitOfWork _uow;
    private readonly ICompanyContext _company;
    private readonly ICurrentUser _user;
    private readonly IEffectiveRightsService _rights;
    private readonly ILogger<TenantConnectionService> _logger;

    // Droits effectifs lus par EnsureAuthorizedAsync pour cette requête (null : administrateur, ou contrôle pas encore fait).
    private EffectiveRights? _effective;

    public TenantConnectionService(
        IUnitOfWork uow, ICompanyContext company, ICurrentUser user,
        IEffectiveRightsService rights, ILogger<TenantConnectionService> logger)
    {
        _uow = uow;
        _company = company;
        _user = user;
        _rights = rights;
        _logger = logger;
    }

    private bool IsAdministrator => _user.RoleId == AdminRules.AdminRoleId;

    public string SageDb => _company.SageDatabase ?? throw new DomainException("En-tête X-Sage-Database requis.");

    /// <summary>Nom de la base pointeuse de la requête ; refusé (403) si le compte ne peut pas l'utiliser.</summary>
    public string PointeuseDb
    {
        get
        {
            var name = _company.PointeuseDatabase ?? throw new DomainException("Aucune base pointeuse configurée.");
            EnsurePointeuseUsable(name);
            return name;
        }
    }

    /// <summary>
    /// Utiliser une base SAGE : administrateur, ou porte 1 effective ET base cochée dans « Accès bases » ET lecture effective.
    /// Refus : ForbiddenException (403), jamais 401. Échec fermé si les droits ne peuvent pas être lus.
    /// </summary>
    public async Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default)
    {
        var sageName = SageDb;
        var sage = (await _uow.Repository<T_BDD_SAGE>().ListAsync(s => s.NOM_BD == sageName, cancellationToken)).FirstOrDefault()
            ?? throw new DomainException("Base SAGE inconnue.");

        if (!IsAdministrator)
        {
            _effective = await LoadRightsOrDenyAsync(cancellationToken);
            if (!_effective.CanUseDatabase(DatabaseKind.Sage, sage.NOM_BD))
                throw new ForbiddenException(AccessDenied);
        }
        await ResolvePointeuseAsync(cancellationToken);
    }

    private async Task<EffectiveRights> LoadRightsOrDenyAsync(CancellationToken cancellationToken)
    {
        if (_user.UserId is not decimal uid)
            throw new ForbiddenException(AccessDenied);
        try
        {
            return await _rights.GetAsync(uid, ct: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _rights.LogReadFailure(uid, ex);
            throw new ForbiddenException(AccessDenied);
        }
    }

    // Pointeuse : administrateur, ou porte 1 effective ET POINTEUSE:<base>:READ effective.
    // Sans EnsureAuthorizedAsync préalable, les droits sont inconnus et l'accès est refusé.
    private void EnsurePointeuseUsable(string name)
    {
        if (IsAdministrator) return;
        if (_effective is null || !_effective.CanUseDatabase(DatabaseKind.Pointeuse, name))
            throw new ForbiddenException(AccessDenied);
    }

    /// <summary>
    /// Fills the pointeuse tenant from the ACTIVE row (or the first configured one)
    /// when the client did not send X-Pointeuse-Database.
    /// </summary>
    public async Task ResolvePointeuseAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_company.PointeuseDatabase))
            return;
        var all = await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(_ => true, cancellationToken);
        var row = all.FirstOrDefault(p => p.ACTIVE == true)
                  ?? all.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.NOM_BD));
        if (row?.NOM_BD is string name && !string.IsNullOrWhiteSpace(_company.SageDatabase))
            _company.Set(_company.SageDatabase, name);
    }

    public async Task<T_BDD_SAGE> GetSageRowAsync(CancellationToken cancellationToken = default)
    {
        var name = SageDb;
        return (await _uow.Repository<T_BDD_SAGE>().ListAsync(s => s.NOM_BD == name, cancellationToken)).FirstOrDefault()
            ?? throw new DomainException("Base SAGE inconnue.");
    }

    public async Task<T_BDD_POINTEUSE> GetPointeuseRowAsync(CancellationToken cancellationToken = default)
    {
        var name = _company.PointeuseDatabase;
        T_BDD_POINTEUSE? row = null;
        if (!string.IsNullOrWhiteSpace(name))
            row = (await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(p => p.NOM_BD == name, cancellationToken)).FirstOrDefault();
        row ??= (await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(p => p.ACTIVE == true, cancellationToken)).FirstOrDefault();
        row ??= (await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(_ => true, cancellationToken)).FirstOrDefault();
        if (row is null) throw new DomainException("Aucune base pointeuse configurée.");
        EnsurePointeuseUsable(row.NOM_BD ?? string.Empty);
        return row;
    }

    public async Task<string> GetSageConnectionAsync(CancellationToken cancellationToken = default)
    {
        var row = await GetSageRowAsync(cancellationToken);
        return ExternalConnectionFactory.Build(row.SERVEUR, row.NOM_BD, row.TYPE_AUTH == true, row.TLOGIN, row.TMDP);
    }

    public async Task<string> GetPointeuseConnectionAsync(CancellationToken cancellationToken = default)
    {
        var row = await GetPointeuseRowAsync(cancellationToken);
        return ExternalConnectionFactory.Build(row.SERVEUR, row.NOM_BD, row.TYPE_AUTH == true, row.TLOGIN, row.TMDP);
    }
}
