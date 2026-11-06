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

        var charges = await LoadChargesAsync(request, cantine, ct);
        if (charges.Count == 0)
            throw new DomainException(cantine
                ? "Aucune cantine calculée sur la période : lancez d'abord le calcul."
                : "Aucun frais de déplacement calculé sur la période : lancez d'abord le calcul.");

        var opCst = await ResolveConstantAsync(sageRow, codeConstante, ct);
        var numSalaries = await MapNumSalarieAsync(sageRow, ct);
        var (employes, cumuls, sansSalarie, sansCumul) = await WriteCumulsAsync(charges, numSalaries, opCst, ct);

        var message = $"{employes} salarié(s) synchronisé(s), {cumuls} cumul(s) mis à jour.";
        if (sansSalarie.Count > 0)
            message += $" {sansSalarie.Count} matricule(s) absent(s) de SAGE : {string.Join(", ", sansSalarie.Take(10))}"
                + (sansSalarie.Count > 10 ? "…" : string.Empty) + ".";
        if (sansCumul.Count > 0)
            message += $" {sansCumul.Count} salarié(s) sans ligne de cumul {codeConstante} (opérande {opCst}).";
        return new ExtrasSyncResultDto(_tenant.SageDb, sageRow.TypeBase ?? string.Empty, employes, cumuls, message);
    }

    /// <summary>Totaux cantine ou frais de déplacement calculés pour la période et la plage de matricules.</summary>
    private async Task<List<ChargeCumul>> LoadChargesAsync(PeriodRequest request, bool cantine, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        if (cantine)
            return (await _uow.Repository<Cantine>().ListAsync(c =>
                    c.NomBddSage == sageDb && c.DateDeb == from && c.DateFin == to, ct))
                .Where(c => MatriculeRange.Contains(c.MatriculeSage, request))
                .Select(c => new ChargeCumul(c.MatriculeSage, c.TotalCantine ?? 0))
                .ToList();
        return (await _uow.Repository<Frais>().ListAsync(c =>
                c.NomBddSage == sageDb && c.DateDeb == from && c.DateFin == to, ct))
            .Where(c => MatriculeRange.Contains(c.MatriculeSage, request))
            .Select(c => new ChargeCumul(c.MatriculeSage, c.TotalPoint ?? 0))
            .ToList();
    }

    /// <summary>Met à jour le cumul de chaque salarié retrouvé dans SAGE et relève les manques.</summary>
    private async Task<(int Employes, int Cumuls, List<string> SansSalarie, List<string> SansCumul)> WriteCumulsAsync(
        List<ChargeCumul> charges, Dictionary<string, int> numSalaries, short opCst, CancellationToken ct)
    {
        var connection = await _tenant.GetSageConnectionAsync(ct);
        var (employes, cumuls) = (0, 0);
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
        return (employes, cumuls, sansSalarie, sansCumul);
    }

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
