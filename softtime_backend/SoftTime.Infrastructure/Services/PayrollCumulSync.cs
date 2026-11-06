using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services;

public sealed class PayrollCumulSync : IPayrollCumulSync
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly ISagePayrollWriter _sageWriter;

    public PayrollCumulSync(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        ISagePayrollWriter sageWriter)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _sageWriter = sageWriter;
    }

    public async Task<ExtrasSyncResultDto> SyncExtrasAsync(PeriodRequest request, string codeConstante, bool cantine, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        ExtraPeriods.EnsurePeriode(request);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(sageRow.TypeBase, "STANDARD", StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "La synchronisation des cumuls cantine / frais n'est possible que pour une base SAGE de type STANDARD.");

        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var charges = cantine
            ? (await _uow.Repository<Cantine>().ListAsync(c =>
                    c.NomBddSage == sageDb && c.DateDeb == from && c.DateFin == to, ct))
                .Where(c => MatriculeRange.Contains(c.MatriculeSage, request))
                .Select(c => new ChargeCumul(c.MatriculeSage, c.TotalCantine ?? 0))
                .ToList()
            : (await _uow.Repository<Frais>().ListAsync(c =>
                    c.NomBddSage == sageDb && c.DateDeb == from && c.DateFin == to, ct))
                .Where(c => MatriculeRange.Contains(c.MatriculeSage, request))
                .Select(c => new ChargeCumul(c.MatriculeSage, c.TotalPoint ?? 0))
                .ToList();
        if (charges.Count == 0)
            throw new DomainException(cantine
                ? "Aucune cantine calculée sur la période : lancez d'abord le calcul."
                : "Aucun frais de déplacement calculé sur la période : lancez d'abord le calcul.");

        var opCst = await ResolveConstantAsync(sageRow, codeConstante, ct);
        var numSalaries = await MapNumSalarieAsync(sageRow, ct);
        var connection = await _tenant.GetSageConnectionAsync(ct);

        var employes = 0;
        var cumuls = 0;
        var sansSalarie = new List<string>();
        var sansCumul = new List<string>();
        foreach (var charge in charges)
        {
            if (!TryNumSalarie(numSalaries, charge.Matricule, out var numSal))
            {
                sansSalarie.Add(charge.Matricule ?? "?");
                continue;
            }
            var updated = await _sageWriter.UpdateCumulAsync(connection, numSal, opCst, charge.Valeur, ct);
            employes++;
            cumuls += updated;
            if (updated == 0)
                sansCumul.Add(charge.Matricule ?? "?");
        }

        var message = $"{employes} salarié(s) synchronisé(s), {cumuls} cumul(s) mis à jour.";
        if (sansSalarie.Count > 0)
            message += $" {sansSalarie.Count} matricule(s) absent(s) de SAGE : {string.Join(", ", sansSalarie.Take(10))}"
                + (sansSalarie.Count > 10 ? "…" : string.Empty) + ".";
        if (sansCumul.Count > 0)
            message += $" {sansCumul.Count} salarié(s) sans ligne de cumul {codeConstante} (opérande {opCst}).";
        return new ExtrasSyncResultDto(sageDb, sageRow.TypeBase ?? string.Empty, employes, cumuls, message);
    }

    /// <summary>Résout la constante SAGE qui porte un cumul, en refusant une constante absente ou incomplète.</summary>
    private async Task<short> ResolveConstantAsync(BddSage sageRow, string code, CancellationToken ct)
    {
        var constants = await _externalSource.GetConstantsAsync(sageRow, ct);
        var match = constants.FirstOrDefault(c => string.Equals(c.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase));
        if (match?.Operande is not short operande)
            throw new DomainException(
                $"Constante SAGE {code} absente ou sans CodeOperande1 : la synchronisation est impossible.");
        return operande;
    }

    /// <summary>Matricule → numéro de salarié SAGE, indexé par clé canonique (zéros de remplissage ignorés).</summary>
    private async Task<Dictionary<string, int>> MapNumSalarieAsync(BddSage sageRow, CancellationToken ct)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var employe in await _externalSource.GetEmployeesAsync(sageRow, ct))
        {
            var cle = MatriculeIdentity.Cle(employe.Matricule);
            if (cle.Length > 0)
                map[cle] = (int)employe.EmployeeId;
        }
        return map;
    }

    private static bool TryNumSalarie(Dictionary<string, int> map, string? matricule, out int numSal)
        => map.TryGetValue(MatriculeIdentity.Cle(matricule), out numSal);
}
