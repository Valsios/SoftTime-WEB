using System.Globalization;
using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Persistence;

/// <summary>Persiste les synthèses d'opérations SQL externes dans le journal applicatif.</summary>
public sealed class AuditTrail : IAuditTrail
{
    private readonly IUnitOfWork _uow;
    private readonly ICompanyContext _company;
    private readonly AuditJournalOptions _options;
    private readonly ILogger<AuditTrail> _logger;

    public AuditTrail(
        IUnitOfWork uow,
        ICompanyContext company,
        AuditJournalOptions options,
        ILogger<AuditTrail> logger)
    {
        _uow = uow;
        _company = company;
        _options = options;
        _logger = logger;
    }

    public async Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        try
        {
            var categoryCounts = string.Join(", ", audit.UpdatesByCategory
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
            var summary = $"Statut={audit.Status}; Période={audit.PeriodFrom:yyyy-MM-dd}..{audit.PeriodTo:yyyy-MM-dd}; "
                + $"LignesMisesÀJour={audit.UpdatedRows}; Catégories={categoryCounts}";

            await _uow.Repository<suivi_JOURNAL>().AddAsync(new suivi_JOURNAL
            {
                DATE_UTC = DateTime.UtcNow,
                TABLES = audit.TargetTable,
                ACTIONS = "SYNC_PAIE",
                NOUVEAUX = summary,
                USERS = string.IsNullOrWhiteSpace(audit.UserLogin) ? "SYSTEME:OvertimeService" : audit.UserLogin,
                BASE_SAGE = audit.SageDatabase ?? _company.SageDatabase,
                BASE_POINTEUSE = _company.PointeuseDatabase,
                CORRELATIONS = audit.Correlation,
                SOURCE = audit.Source
            }, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Impossible d'écrire la synthèse d'audit de synchronisation paie {Status} vers {TargetTable}.",
                audit.Status,
                audit.TargetTable);
        }
    }
}
