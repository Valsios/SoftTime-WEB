using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Overtime;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Overtime;

public sealed class OvertimePayrollSyncService : IOvertimePayrollSyncService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IPayrollWriterResolver _payrollWriters;
    private readonly ICurrentUser? _user;
    private readonly IOvertimeWeekContext _overtimeWeekContext;
    private readonly IExternalSourceService? _externalSource;

    public OvertimePayrollSyncService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IPayrollWriterResolver payrollWriters,
        ICurrentUser? user,
        IOvertimeWeekContext overtimeWeekContext,
        IExternalSourceService? externalSource = null)
    {
        _externalSource = externalSource;
        _uow = uow;
        _tenant = tenant;
        _payrollWriters = payrollWriters;
        _user = user;
        _overtimeWeekContext = overtimeWeekContext;
    }

    // Endpoint historique : conserve pour les integrations existantes, uniquement pour STANDARD.
    public async Task SyncSageAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(sageRow.TypeBase, "STANDARD", StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "La synchronisation SAGE est disponible uniquement pour une base STANDARD. Utilisez la synchronisation de paie pour une base AUTRE.");
        await SyncPayrollAsync(request, ct);
    }

    // Ecriture de paie unifiee : le resolveur choisit le writer STANDARD (T_CUMSAL) ou AUTRE (T_PAIE_ECRITURE).
    public async Task<PayrollWriteResultDto> SyncPayrollAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        _overtimeWeekContext.EnsureMaxOneMonth(request);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var rows = await _uow.Repository<HsExoImp>().ListReadOnlyAsync(h =>
            h.BddSage == _tenant.SageDb && h.PeriodeDebut == request.From.Date && h.PeriodeFin == request.To.Date, ct);

        // Périmètre de l'envoi : les cartes de la période, du département et des matricules demandés. Un salarié du
        // périmètre sans ligne de valeurs reçoit une ligne synthétique (valeurs nulles) : les variables cochées sont
        // remises à 0, pour que l'ancien cumul d'un envoi précédent ne reste pas dans la paie. Un salarié hors du
        // périmètre n'est pas touché.
        var cards = await _overtimeWeekContext.TargetCardsAsync(request, ct);
        if (!string.IsNullOrEmpty(request.Departement))
        {
            var targetMatricules = cards.Select(c => c.SageMatricule).Where(m => !string.IsNullOrEmpty(m)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows = rows.Where(h => h.Matricule != null && targetMatricules.Contains(h.Matricule)).ToList();
        }

        var dtos = rows
            .Select(h => new HsExoDto(h.Oid, h.Matricule, h.NumSal, h.Exo130, h.Exo150, h.I130, h.I150,
                h.Ferie, h.Nuit, h.Dim, h.Retard, h.Absence))
            .ToList();
        dtos.AddRange(await LignesSansValeurAsync(sageRow, cards, rows, ct));
        var auditContext = new PayrollSyncContext(request.From.Date, request.To.Date, _user?.Login, Guid.NewGuid());
        return await _payrollWriters.Resolve(sageRow).WriteAsync(sageRow, dtos, auditContext, ct);
    }

    /// <summary>
    /// Une ligne synthétique par carte du périmètre sans ligne de valeurs sur la période. Le numéro de salarié (base
    /// STANDARD) vient de SAGE ; sans lui, la ligne est ignorée à l'écriture. Une lecture SAGE impossible ne bloque pas
    /// l'envoi : les salariés sans valeur ne sont alors pas remis à 0 en STANDARD.
    /// </summary>
    private async Task<List<HsExoDto>> LignesSansValeurAsync(
        BddSage sageRow, IReadOnlyList<CardPaie> cards, IReadOnlyList<HsExoImp> rows, CancellationToken ct)
    {
        var avecLigne = rows.Select(h => MatriculeIdentity.Cle(h.Matricule)).ToHashSet(StringComparer.Ordinal);
        var sansLigne = cards.Where(c => !avecLigne.Contains(MatriculeIdentity.Cle(c.SageMatricule))).ToList();
        if (sansLigne.Count == 0)
            return new List<HsExoDto>();
        var numeros = new Dictionary<string, int>(StringComparer.Ordinal);
        if (_externalSource is not null)
        {
            try
            {
                foreach (var employe in await _externalSource.GetEmployeesAsync(sageRow, ct))
                    numeros[MatriculeIdentity.Cle(employe.Matricule)] = (int)employe.EmployeeId;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                numeros.Clear();
            }
        }
        return sansLigne
            .Select(c => new HsExoDto(0, c.SageMatricule, numeros.TryGetValue(MatriculeIdentity.Cle(c.SageMatricule), out var n) ? n : null,
                0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m) { SansValeur = true })
            .ToList();
    }
}
