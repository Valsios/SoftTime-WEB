using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

public class ExtraService
{
    /// <summary>Constante SAGE portant le cumul cantine (même code que le desktop).</summary>
    public const string CodeConstanteCantine = "CANTINE_CODE";

    /// <summary>Constante SAGE portant le cumul des frais de déplacement (même code que le desktop).</summary>
    public const string CodeConstanteFrais = "HA06";

    private readonly IUnitOfWork _uow;
    private readonly TenantConnectionService _tenant;
    private readonly IPunchAggregate _punches;
    private readonly IExternalSourceService _externalSource;
    private readonly ISagePayrollWriter _sageWriter;

    public ExtraService(
        IUnitOfWork uow,
        TenantConnectionService tenant,
        IPunchAggregate punches,
        IExternalSourceService externalSource,
        ISagePayrollWriter sageWriter)
    {
        _uow = uow;
        _tenant = tenant;
        _punches = punches;
        _externalSource = externalSource;
        _sageWriter = sageWriter;
    }

    public async Task<IReadOnlyList<CanteenDto>> ListCanteenAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var list = await _uow.Repository<T_CANTINE>().ListAsync(c => c.NOM_BDD_SAGE == _tenant.SageDb, ct);
        return list
            .Where(c => c.DATEDEB?.Date >= from && c.DATEFIN?.Date <= to
                        && InMatriculeRange(c.MATRICULE_SAGE, request.MatriculeFrom, request.MatriculeTo))
            .OrderBy(c => c.MATRICULE_SAGE)
            .Select(c => new CanteenDto(c.IDCANTINE, c.MATRICULE_SAGE, c.DATEDEB, c.DATEFIN, c.TOTAL_CANTINE))
            .ToList();
    }

    public async Task<IReadOnlyList<TravelDto>> ListTravelAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var list = await _uow.Repository<T_FRAIS>().ListAsync(c => c.NOM_BDD_SAGE == _tenant.SageDb, ct);
        return list
            .Where(c => c.DATEDEB?.Date >= from && c.DATEFIN?.Date <= to
                        && InMatriculeRange(c.MATRICULE_SAGE, request.MatriculeFrom, request.MatriculeTo))
            .OrderBy(c => c.MATRICULE_SAGE)
            .Select(c => new TravelDto(c.IDPOINTAGE, c.MATRICULE_SAGE, c.DATEDEB, c.DATEFIN, c.TOTAL_POINT))
            .ToList();
    }

    public async Task<IReadOnlyList<CanteenDto>> ComputeCanteenAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var jours = await _punches.ListCanteenDaysAsync(_tenant.SageDb, from, to.AddDays(1), ct);
        var counts = jours
            .GroupBy(j => j.Matricule)
            .Select(g => new PunchCountDto(g.Key, g.Select(j => j.Date.Date).Distinct().Count()))
            .ToList();
        var repo = _uow.Repository<T_CANTINE>();
        var old = await repo.ListAsync(
            c => c.NOM_BDD_SAGE == _tenant.SageDb && c.DATEDEB == from && c.DATEFIN == to, ct);
        repo.RemoveRange(old);
        var result = new List<CanteenDto>();
        foreach (var row in counts)
        {
            var entity = new T_CANTINE
            {
                NOM_BDD_SAGE = _tenant.SageDb,
                NOM_BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULE_SAGE = row.Matricule,
                DATEDEB = from,
                DATEFIN = to,
                TOTAL_CANTINE = row.Total
            };
            await repo.AddAsync(entity, ct);
            result.Add(new CanteenDto(entity.IDCANTINE, entity.MATRICULE_SAGE, entity.DATEDEB, entity.DATEFIN, entity.TOTAL_CANTINE));
        }
        await _uow.SaveChangesAsync(ct);
        return result.OrderBy(r => r.Matricule).ToList();
    }

    public async Task<IReadOnlyList<TravelDto>> ComputeTravelAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var counts = await _punches.CountTravelPunchesAsync(_tenant.SageDb, from, to.AddDays(1), ct);
        var repo = _uow.Repository<T_FRAIS>();
        var old = await repo.ListAsync(
            c => c.NOM_BDD_SAGE == _tenant.SageDb && c.DATEDEB == from && c.DATEFIN == to, ct);
        repo.RemoveRange(old);
        var result = new List<TravelDto>();
        foreach (var row in counts)
        {
            var entity = new T_FRAIS
            {
                NOM_BDD_SAGE = _tenant.SageDb,
                NOM_BDD_POINTEUSE = _tenant.PointeuseDb,
                MATRICULE_SAGE = row.Matricule,
                DATEDEB = from,
                DATEFIN = to,
                TOTAL_POINT = row.Total
            };
            await repo.AddAsync(entity, ct);
            result.Add(new TravelDto(entity.IDPOINTAGE, entity.MATRICULE_SAGE, entity.DATEDEB, entity.DATEFIN, entity.TOTAL_POINT));
        }
        await _uow.SaveChangesAsync(ct);
        return result.OrderBy(r => r.Matricule).ToList();
    }

    private static bool InMatriculeRange(string? matricule, string? from, string? to)
    {
        if (string.IsNullOrEmpty(matricule))
            return false;
        if (!string.IsNullOrEmpty(from) && string.Compare(matricule, from, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (!string.IsNullOrEmpty(to) && string.Compare(matricule, to, StringComparison.OrdinalIgnoreCase) > 0)
            return false;
        return true;
    }

    /// <summary>
    /// Anomalies du calcul cantine : jour où un pointage tombe dans la fenêtre du repas alors que
    /// les heures corrigées déclarent une absence complète (matin et après-midi).
    /// Le desktop remontait une anomalie pour <b>toute</b> absence complète ; ici seules les
    /// contradictions entre les deux sources sont signalées, sinon chaque journée d'absence
    /// produirait une ligne inutile. Les anomalies sont dérivées, jamais stockées.
    /// </summary>
    public async Task<IReadOnlyList<CanteenAnomalyDto>> ListCanteenAnomaliesAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var from = request.From.Date;
        var to = request.To.Date;
        var sageDb = _tenant.SageDb;

        var joursPointes = (await _punches.ListCanteenDaysAsync(sageDb, from, to.AddDays(1), ct))
            .Where(j => MatriculeRange.Contains(j.Matricule, request))
            .ToList();

        var corrections = await _uow.Repository<T_HEURECORRIGER>().ListAsync(h =>
            h.BDD_SAGE == sageDb
            && h.DATE_POINTAGE_IN != null
            && h.DATE_POINTAGE_IN >= from
            && h.DATE_POINTAGE_IN < to.AddDays(1), ct);
        var absences = corrections
            .Where(h => CanteenRules.EstAbsenceComplete(h.ABSENCE_AM, h.ABSENCE_PM))
            .Select(h => new CanteenDay(h.MATRICULE_SAGE ?? string.Empty, h.DATE_POINTAGE_IN!.Value.Date))
            .Where(d => d.Matricule.Length > 0 && MatriculeRange.Contains(d.Matricule, request))
            .ToList();

        return CanteenRules.DetecterAnomalies(joursPointes, absences)
            .Select(a => new CanteenAnomalyDto(a.Matricule, a.Date, a.Motif))
            .ToList();
    }

    /// <summary>
    /// Purge les lignes cantine de la période (portage de <c>PurgerCantines</c> du desktop, qui
    /// filtrait sur <c>DateDebut &gt;= from</c>, <c>DateFin &lt;= to</c> et la plage de matricules).
    /// Une période vide est refusée : elle viderait toute la table.
    /// </summary>
    public async Task<PurgeExtrasResultDto> PurgeCanteenAsync(PeriodRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsurePeriode(request);
        var repo = _uow.Repository<T_CANTINE>();
        var from = request.From.Date;
        var to = request.To.Date;
        var lignes = (await repo.ListAsync(c =>
                c.NOM_BDD_SAGE == _tenant.SageDb && c.DATEDEB >= from && c.DATEFIN <= to, ct))
            .Where(c => MatriculeRange.Contains(c.MATRICULE_SAGE, request))
            .ToList();
        if (lignes.Count == 0)
            return new PurgeExtrasResultDto(0, "Aucune cantine à purger sur la période.");
        repo.RemoveRange(lignes);
        await _uow.SaveChangesAsync(ct);
        return new PurgeExtrasResultDto(lignes.Count, $"{lignes.Count} ligne(s) cantine supprimée(s).");
    }

    /// <summary>
    /// Pousse les totaux cantine de la période dans le cumul SAGE (<c>T_CUMSAL</c>) référencé par la
    /// constante <c>CANTINE_CODE</c>. Portage de <c>SynchroniserCantines</c> du desktop.
    /// <para>
    /// Écarts assumés : (1) les valeurs poussées sont les lignes <b>stockées</b> de la période, et non
    /// le corps de la requête comme le desktop (qui faisait confiance au client) ; (2) la valeur poussée
    /// est le <b>total calculé</b> (<c>TOTAL_CANTINE</c>) alors que le desktop écrivait la constante
    /// <c>'1'</c> marquée dans son code source comme provisoire — à confirmer avec le client ;
    /// (3) la constante SAGE absente provoque un refus, au lieu du cumul arbitraire 123 du desktop.
    /// </para>
    /// </summary>
    public Task<ExtrasSyncResultDto> SyncCanteenAsync(PeriodRequest request, CancellationToken ct = default)
        => SyncExtrasAsync(request, CodeConstanteCantine, cantine: true, ct);

    /// <summary>
    /// Pousse les totaux de frais de déplacement de la période dans le cumul SAGE (<c>T_CUMSAL</c>)
    /// référencé par la constante <c>HA06</c>. Portage de <c>SynchroniserAvecSageAsync</c> du desktop.
    /// </summary>
    public Task<ExtrasSyncResultDto> SyncTravelAsync(PeriodRequest request, CancellationToken ct = default)
        => SyncExtrasAsync(request, CodeConstanteFrais, cantine: false, ct);

    private async Task<ExtrasSyncResultDto> SyncExtrasAsync(PeriodRequest request, string codeConstante, bool cantine, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        EnsurePeriode(request);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(sageRow.TYPE_BASE, "STANDARD", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "La synchronisation des cumuls cantine / frais n'est possible que pour une base SAGE de type STANDARD.");

        var sageDb = _tenant.SageDb;
        var from = request.From.Date;
        var to = request.To.Date;
        var charges = cantine
            ? (await _uow.Repository<T_CANTINE>().ListAsync(c =>
                    c.NOM_BDD_SAGE == sageDb && c.DATEDEB == from && c.DATEFIN == to, ct))
                .Where(c => MatriculeRange.Contains(c.MATRICULE_SAGE, request))
                .Select(c => new ChargeCumul(c.MATRICULE_SAGE, c.TOTAL_CANTINE ?? 0))
                .ToList()
            : (await _uow.Repository<T_FRAIS>().ListAsync(c =>
                    c.NOM_BDD_SAGE == sageDb && c.DATEDEB == from && c.DATEFIN == to, ct))
                .Where(c => MatriculeRange.Contains(c.MATRICULE_SAGE, request))
                .Select(c => new ChargeCumul(c.MATRICULE_SAGE, c.TOTAL_POINT ?? 0))
                .ToList();
        if (charges.Count == 0)
            throw new InvalidOperationException(cantine
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
        return new ExtrasSyncResultDto(sageDb, sageRow.TYPE_BASE ?? string.Empty, employes, cumuls, message);
    }

    private sealed record ChargeCumul(string? Matricule, int Valeur);

    /// <summary>Résout la constante SAGE qui porte un cumul, en refusant une constante absente ou incomplète.</summary>
    private async Task<short> ResolveConstantAsync(T_BDD_SAGE sageRow, string code, CancellationToken ct)
    {
        var constants = await _externalSource.GetConstantsAsync(sageRow, ct);
        var match = constants.FirstOrDefault(c => string.Equals(c.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase));
        if (match?.Operande is not short operande)
            throw new InvalidOperationException(
                $"Constante SAGE {code} absente ou sans CodeOperande1 : la synchronisation est impossible.");
        return operande;
    }

    /// <summary>Matricule → numéro de salarié SAGE, indexé par clé canonique (zéros de remplissage ignorés).</summary>
    private async Task<Dictionary<string, int>> MapNumSalarieAsync(T_BDD_SAGE sageRow, CancellationToken ct)
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

    /// <summary>Refuse une période vide : <c>DateTime.MinValue</c> en bornes viderait toute la table.</summary>
    private static void EnsurePeriode(PeriodRequest request)
    {
        if (request.From == default || request.To == default)
            throw new InvalidOperationException("Période requise (du … au …).");
        if (request.To.Date < request.From.Date)
            throw new InvalidOperationException("La date de fin doit suivre la date de début.");
    }
}
