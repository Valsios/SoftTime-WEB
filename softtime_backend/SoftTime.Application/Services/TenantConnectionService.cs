using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

public class TenantConnectionService
{
    private readonly IUnitOfWork _uow;
    private readonly ICompanyContext _company;
    private readonly ICurrentUser _user;

    public TenantConnectionService(IUnitOfWork uow, ICompanyContext company, ICurrentUser user)
    {
        _uow = uow;
        _company = company;
        _user = user;
    }

    public string SageDb => _company.SageDatabase ?? throw new InvalidOperationException("En-tête X-Sage-Database requis.");
    public string PointeuseDb =>
        _company.PointeuseDatabase ?? throw new InvalidOperationException("Aucune base pointeuse configurée.");

    public async Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default)
    {
        var sageName = SageDb;
        var sage = (await _uow.Repository<T_BDD_SAGE>().ListAsync(s => s.NOM_BD == sageName, cancellationToken)).FirstOrDefault()
            ?? throw new InvalidOperationException("Base SAGE inconnue.");
        if (_user.UserId is decimal uid)
        {
            var ok = (await _uow.Repository<T_BDD_AUTORISE>().ListAsync(a => a.IDRESPONSABLE == uid && a.IDBDD == sage.ID, cancellationToken)).Count > 0;
            if (!ok)
                throw new UnauthorizedAccessException("Base SAGE non autorisée pour cet utilisateur.");
        }
        await ResolvePointeuseAsync(cancellationToken);
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
            ?? throw new InvalidOperationException("Base SAGE inconnue.");
    }

    public async Task<T_BDD_POINTEUSE> GetPointeuseRowAsync(CancellationToken cancellationToken = default)
    {
        var name = _company.PointeuseDatabase;
        T_BDD_POINTEUSE? row = null;
        if (!string.IsNullOrWhiteSpace(name))
            row = (await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(p => p.NOM_BD == name, cancellationToken)).FirstOrDefault();
        row ??= (await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(p => p.ACTIVE == true, cancellationToken)).FirstOrDefault();
        row ??= (await _uow.Repository<T_BDD_POINTEUSE>().ListAsync(_ => true, cancellationToken)).FirstOrDefault();
        return row ?? throw new InvalidOperationException("Aucune base pointeuse configurée.");
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

