using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services.Payroll;

/// <summary>
/// Contrôle commun « période clôturée » (T_PAIE_CLOTURE) de la base active. Service à portée de requête : les périodes
/// sont lues UNE fois par requête (une requête SQL indexée sur la base), puis chaque date est contrôlée en mémoire ; un lot
/// de milliers de dates ne coûte donc qu'une lecture. Lecture seule ; un refus lève une ConflictException (409).
/// </summary>
public sealed class PayrollClosureGuard : IPayrollClosureGuard
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private IReadOnlyList<ClosedPeriod>? _periodes;
    private string? _base;

    public PayrollClosureGuard(IUnitOfWork uow, ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ClosedPeriod>> ClosedPeriodsAsync(CancellationToken ct = default)
    {
        var baseSage = _tenant.SageDb;
        if (string.IsNullOrWhiteSpace(baseSage))
        {
            await _tenant.EnsureAuthorizedAsync(ct);
            baseSage = _tenant.SageDb;
        }
        if (_periodes is not null && string.Equals(_base, baseSage, StringComparison.Ordinal))
            return _periodes;
        var clotures = await _uow.Repository<PaieCloture>().ListReadOnlyAsync(c => c.BddSage == baseSage, ct);
        _base = baseSage;
        _periodes = clotures
            .Select(c => new ClosedPeriod(c.PeriodeDebut.Date, c.PeriodeFin.Date))
            .OrderBy(p => p.Debut)
            .ToList();
        return _periodes;
    }

    public async Task<ClosedPeriod?> FindOverlapAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var (debut, fin) = from.Date <= to.Date ? (from.Date, to.Date) : (to.Date, from.Date);
        return (await ClosedPeriodsAsync(ct)).FirstOrDefault(p => p.Recoupe(debut, fin));
    }

    public async Task EnsureOpenAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (await FindOverlapAsync(from, to, ct) is { } periode)
            throw new ConflictException(periode.MessageModification);
    }

    public async Task EnsureOpenAsync(IEnumerable<DateTime> dates, CancellationToken ct = default)
    {
        var periodes = await ClosedPeriodsAsync(ct);
        if (periodes.Count == 0)
            return;
        foreach (var date in dates)
        {
            if (periodes.FirstOrDefault(p => p.Contient(date)) is { } periode)
                throw new ConflictException(periode.MessageModification);
        }
    }

    /// <summary>Oublie les périodes lues (après une clôture dans la même requête).</summary>
    internal void Oublier() => _periodes = null;
}
