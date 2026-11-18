using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Extras;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.Queries;

namespace SoftTime.Infrastructure.Services.Extras;

public sealed class PayrollCumulSync : IPayrollCumulSync
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;
    private readonly ISagePayrollWriter _sageWriter;

    private readonly IPayrollClosureGuard? _closures;
    private readonly IPayrollSendLog? _sendLog;
    private readonly ICurrentUser? _user;

    public PayrollCumulSync(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource,
        ISagePayrollWriter sageWriter,
        IPayrollClosureGuard? closures = null,
        IPayrollSendLog? sendLog = null,
        ICurrentUser? user = null)
    {
        _closures = closures;
        _sendLog = sendLog;
        _user = user;
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
        _sageWriter = sageWriter;
    }

    public async Task<ExtrasSyncResultDto> SyncExtrasAsync(PeriodRequest request, string codeConstante, bool cantine, CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        ExtraPeriods.EnsurePeriode(request);
        // Clôture de paie : aucun envoi de cantine ou de frais (T_CUMSAL) sur une période qui recoupe une période clôturée.
        await ClosureChecks.OuverteAsync(_closures, request.From, request.To, ct);
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

        // Marqueur d'envoi (domaine CANTINE ou FRAIS) posé EN_COURS avant la première écriture : les écritures T_CUMSAL ne
        // sont pas transactionnelles, un échec en cours d'envoi laisse donc un envoi visible (ECHEC).
        var domaine = cantine ? PayrollDomains.Cantine : PayrollDomains.Frais;
        var envoi = new EnvoiCumuls(domaine, codeConstante.Trim());
        var marqueur = await DebuterAsync(request, domaine, ct);
        try
        {
            await WriteCumulsAsync(charges, numSalaries, opCst, envoi, ct);
        }
        catch (Exception)
        {
            await TerminerAsync(marqueur, "ECHEC", envoi.Cumuls);
            throw;
        }
        var (employes, cumuls, sansSalarie, sansCumul) = (envoi.Employes, envoi.Cumuls, envoi.SansSalarie, envoi.SansCumul);
        var fige = await FigerAsync(marqueur, envoi, request);

        var message = $"{employes} salarié(s) synchronisé(s), {cumuls} cumul(s) mis à jour.";
        if (sansSalarie.Count > 0)
            message += $" {sansSalarie.Count} matricule(s) absent(s) de SAGE : {string.Join(", ", sansSalarie.Take(10))}"
                + (sansSalarie.Count > 10 ? "…" : string.Empty) + ".";
        if (sansCumul.Count > 0)
            message += $" {sansCumul.Count} salarié(s) sans ligne de cumul {codeConstante} (opérande {opCst}).";
        if (!fige)
            message += " " + StandardPayrollWriter.InstantaneNonEcrit;
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

    /// <summary>
    /// Met à jour le cumul de chaque salarié retrouvé dans SAGE et relève les manques. Chaque valeur qui a réellement
    /// modifié une ligne de T_CUMSAL est ajoutée à l'instantané ; une mise à jour sans ligne est seulement comptée.
    /// </summary>
    private async Task WriteCumulsAsync(
        List<ChargeCumul> charges, Dictionary<string, int> numSalaries, short opCst, EnvoiCumuls envoi, CancellationToken ct)
    {
        var connection = await _tenant.GetSageConnectionAsync(ct);
        foreach (var charge in charges)
        {
            if (!TryNumSalarie(numSalaries, charge.Matricule, out var numSal))
            {
                envoi.SansSalarie.Add(charge.Matricule ?? "?");
                continue;
            }
            var updated = await _sageWriter.UpdateCumulAsync(connection, numSal, opCst, charge.Valeur, ct);
            envoi.Employes++;
            envoi.Cumuls += updated;
            if (updated == 0)
                envoi.SansCumul.Add(charge.Matricule ?? "?");
            else
                envoi.Instantane.Ecrite(charge.Matricule, numSal, envoi.Domaine, envoi.Destination, PayrollDomains.UniteNombre,
                    charge.Valeur, remiseAZero: false);
        }
    }

    /// <summary>Pose le marqueur EN_COURS (rien sans service d'envoi). Un échec arrête l'envoi avant toute écriture.</summary>
    private async Task<int?> DebuterAsync(PeriodRequest request, string domaine, CancellationToken ct)
    {
        if (_sendLog is null)
            return null;
        return await _sendLog.RecordAsync(new PayrollSend(_tenant.SageDb, "STANDARD", request.From.Date, request.To.Date,
            _user?.Login, "EN_COURS", 0, domaine), ct);
    }

    /// <summary>
    /// Fin d'un envoi réussi : statut SUCCES ET instantané dans une seule transaction SoftTime. Si elle échoue (période
    /// clôturée entre-temps, base indisponible…), l'envoi est marqué INCERTAIN (au mieux : sinon il reste EN_COURS, ce qui ne
    /// permet pas non plus de clôturer). Ne lève jamais : la paie est déjà écrite. Faux si l'instantané n'a pas été écrit.
    /// </summary>
    private async Task<bool> FigerAsync(int? marqueur, EnvoiCumuls envoi, PeriodRequest request)
    {
        if (_sendLog is null || marqueur is not int id)
            return true;
        var instantane = new PayrollSnapshotDraft(envoi.Instantane.Lignes.ToList(), envoi.SansCumul.Count, 0,
            PayrollSendFilter.De(null, request.MatriculeFrom, request.MatriculeTo), RemiseAZero: false, HeuresParJour: 0m);
        try
        {
            await _sendLog.CompleteWithSnapshotAsync(id, "SUCCES", envoi.Cumuls, instantane, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            // Déjà journalisé par le service du marqueur.
            await TerminerAsync(marqueur, "INCERTAIN", envoi.Cumuls);
            return false;
        }
    }

    /// <summary>Statut final sans instantané (ECHEC, INCERTAIN). Ne lève jamais : l'issue d'origine doit rester celle rendue.</summary>
    private async Task TerminerAsync(int? marqueur, string statut, int lignes)
    {
        if (_sendLog is null || marqueur is not int id)
            return;
        try
        {
            await _sendLog.CompleteAsync(id, statut, lignes, CancellationToken.None);
        }
        catch (Exception)
        {
            // Reste EN_COURS : déjà journalisé par le service du marqueur.
        }
    }

    /// <summary>État d'un envoi de cumuls : comptages et lignes réellement écrites (instantané).</summary>
    private sealed class EnvoiCumuls(string domaine, string destination)
    {
        public string Domaine { get; } = domaine;
        public string Destination { get; } = destination;
        public int Employes { get; set; }
        public int Cumuls { get; set; }
        public List<string> SansSalarie { get; } = new();
        public List<string> SansCumul { get; } = new();
        public PayrollSnapshotBuilder Instantane { get; } = new();
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
