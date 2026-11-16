using System.Runtime.ExceptionServices;

using SoftTime.Application.Abstractions.Audit;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Integrations;

/// <summary>
/// Adaptateur qui préserve l'écriture historique T_CUMSAL. Seules les variables cochées pour la base sont validées et
/// écrites ; sans configuration, les sept variables d'heures supplémentaires le sont, comme avant.
/// </summary>
public class StandardPayrollWriter : IPayrollWriter
{
    private readonly ISagePayrollWriter _sageWriter;
    private readonly ISageContextFactory _sageFactory;
    private readonly ITenantConnectionService _tenant;
    private readonly ICodeConstanteService _catalog;
    private readonly IAuditTrail _auditTrail;
    private readonly IPayrollVariableService? _variables;
    private readonly IPayrollSendLog? _sendLog;

    public StandardPayrollWriter(
        ISagePayrollWriter sageWriter,
        ISageContextFactory sageFactory,
        ITenantConnectionService tenant,
        ICodeConstanteService catalog,
        IAuditTrail auditTrail,
        IPayrollVariableService? variables = null,
        IPayrollSendLog? sendLog = null)
    {
        _sageWriter = sageWriter;
        _sageFactory = sageFactory;
        _tenant = tenant;
        _catalog = catalog;
        _auditTrail = auditTrail;
        _variables = variables;
        _sendLog = sendLog;
    }

    public bool CanHandle(BddSage database) => string.Equals(database.TypeBase, "STANDARD", StringComparison.OrdinalIgnoreCase);

    public async Task<PayrollWriteResultDto> ValidateAsync(BddSage database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
    {
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var constants = sage.Constants.ToArray();
        var codes = await _catalog.GetOvertimeSageCodesAsync(ct);
        var selection = await SelectionAsync(database, ct);
        var cible = new SagePayrollTarget(string.Empty, constants, codes) { Variables = selection };
        // Une variable décochée (ou non envoyable) n'est ni validée ni comptée comme manquante.
        foreach (var code in selection.Envoyees.Select(cible.CodeDe))
            if (!constants.Any(c => string.Equals(c.CodeConstante?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
                throw new DomainException($"Constante SAGE {code} introuvable.");
        return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "STANDARD", 0, 0, "Configuration SAGE valide.");
    }

    public async Task<PayrollWriteResultDto> WriteAsync(BddSage database, IReadOnlyList<HsExoDto> rows, PayrollSyncContext auditContext, CancellationToken ct = default)
    {
        // Le flux ci-dessous est intentionnellement celui qui existait dans OvertimeService.
        var input = (await GetInputAsync(ct)) with { Variables = await SelectionAsync(database, ct) };
        var categoryCounts = PayrollWritingConfiguration.CompteursParCategorie(input.Variables);
        var envoi = new SendMarker(_sendLog, database, auditContext);
        var sansLigne = 0;
        try
        {
            foreach (var hs in rows.Where(h => h.NumSal.HasValue))
            {
                var updates = await _sageWriter.SyncOvertimeAsync(input, hs.NumSal!.Value, OvertimeAmounts.From(hs), ct);
                foreach (var (category, count) in updates)
                {
                    categoryCounts[category] += count;
                    if (count == 0)
                        sansLigne++;
                }
                // Marqueur posé dès la première ligne T_CUMSAL modifiée ; s'il ne peut pas l'être, l'envoi s'arrête.
                await envoi.SignalerAsync(categoryCounts.Values.Sum(), ct);
            }
        }
        catch (PayrollSyncWriteException exception)
        {
            foreach (var (category, count) in exception.UpdatesByCategory)
                categoryCounts[category] += count;
            await envoi.TerminerAsync("ECHEC", categoryCounts.Values.Sum());
            await WriteAuditAsync(database, auditContext, new PayrollSyncOutcome(rows.Count(h => h.NumSal.HasValue), categoryCounts.Values.Sum(), 0, categoryCounts, "ECHEC"));
            ExceptionDispatchInfo.Capture(exception.InnerException ?? exception).Throw();
            throw;
        }
        catch
        {
            await envoi.TerminerAsync("ECHEC", categoryCounts.Values.Sum());
            await WriteAuditAsync(database, auditContext, new PayrollSyncOutcome(rows.Count(h => h.NumSal.HasValue), categoryCounts.Values.Sum(), 0, categoryCounts, "ECHEC"));
            throw;
        }

        var written = rows.Count(h => h.NumSal.HasValue);
        await envoi.TerminerAsync("SUCCES", categoryCounts.Values.Sum());
        await WriteAuditAsync(database, auditContext, new PayrollSyncOutcome(written, categoryCounts.Values.Sum(), 0, categoryCounts, "SUCCES"));
        var message = "Synchronisation des heures supplémentaires vers SAGE terminée.";
        if (sansLigne > 0)
            message += $" Attention : {sansLigne} mise(s) à jour n'ont modifié aucune ligne T_CUMSAL "
                       + "(cumul absent dans SAGE pour le salarié) : vérifiez ces cumuls dans SAGE.";
        return new PayrollWriteResultDto(database.NomBd ?? string.Empty, "STANDARD", written, categoryCounts.Values.Sum(), message)
        {
            UpdatesWithoutRow = sansLigne
        };
    }

    protected virtual async Task<SagePayrollTarget> GetInputAsync(CancellationToken ct)
    {
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        return new SagePayrollTarget(
            await _tenant.GetSageConnectionAsync(ct),
            sage.Constants.ToArray(),
            await _catalog.GetOvertimeSageCodesAsync(ct));
    }

    private async Task<PayrollVariableSelection> SelectionAsync(BddSage database, CancellationToken ct) =>
        _variables is null || string.IsNullOrWhiteSpace(database.NomBd)
            ? PayrollVariableSelection.Default
            : await _variables.GetSelectionAsync(database.NomBd, ct);

    private Task WriteAuditAsync(BddSage database, PayrollSyncContext context, PayrollSyncOutcome bilan)
        => _auditTrail.WritePayrollSyncAsync(new PayrollSyncAudit(
            "T_CUMSAL", database.NomBd, context.PeriodFrom, context.PeriodTo, bilan.Employees, bilan.UpdatedRows, 0,
            bilan.Categories, bilan.Status, context.UserLogin, "SYNC_PAIE:STANDARD", context.Correlation), CancellationToken.None);

    /// <summary>
    /// Marqueur T_PAIE_ENVOI d'un envoi STANDARD : posé (EN_COURS) à la première ligne modifiée, puis terminé. Les
    /// écritures T_CUMSAL ne sont pas transactionnelles : un échec partiel reste donc « envoyé ».
    /// </summary>
    private sealed class SendMarker(IPayrollSendLog? log, BddSage database, PayrollSyncContext context)
    {
        private int? _id;

        public async Task SignalerAsync(int lignes, CancellationToken ct)
        {
            if (log is null || _id.HasValue || lignes <= 0)
                return;
            _id = await log.RecordAsync(new PayrollSend(database.NomBd ?? string.Empty, "STANDARD",
                context.PeriodFrom, context.PeriodTo, context.UserLogin, "EN_COURS", lignes), CancellationToken.None);
        }

        /// <summary>
        /// Fin de l'envoi : enregistre l'envoi s'il manque encore (échec partiel), puis son statut final. Ne lève
        /// jamais : la paie est déjà écrite et l'issue d'origine (succès ou erreur) doit rester celle rendue. Un échec
        /// du marqueur est journalisé par <see cref="IPayrollSendLog"/> ; une ligne restée EN_COURS compte comme envoyée.
        /// </summary>
        public async Task TerminerAsync(string statut, int lignes)
        {
            if (log is null)
                return;
            try
            {
                await SignalerAsync(lignes, CancellationToken.None);
                if (_id.HasValue)
                    await log.CompleteAsync(_id.Value, statut, lignes, CancellationToken.None);
            }
            catch (Exception)
            {
                // Déjà journalisé par le service du marqueur.
            }
        }
    }
}
