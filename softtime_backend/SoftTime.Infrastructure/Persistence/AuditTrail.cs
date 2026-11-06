using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Persistence;

/// <summary>Persiste les synthèses d'opérations SQL externes dans le journal applicatif.</summary>
public sealed class AuditTrail : IAuditTrail
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICompanyContext _company;
    private readonly AuditJournalOptions _options;
    private readonly ILogger<AuditTrail> _logger;

    public AuditTrail(
        IServiceScopeFactory scopeFactory,
        ICompanyContext company,
        IOptions<AuditJournalOptions> options,
        ILogger<AuditTrail> logger)
    {
        _scopeFactory = scopeFactory;
        _company = company;
        _options = options.Value;
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
                + $"Salariés={audit.Employees}; LignesMisesÀJour={audit.UpdatedRows}; "
                + (audit.CancelledRows > 0 ? $"LignesAnnulées={audit.CancelledRows}; " : string.Empty)
                + $"Catégories={categoryCounts}";
            var sageDatabase = audit.SageDatabase ?? _company.SageDatabase;
            var pointeuseDatabase = _company.PointeuseDatabase;

            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.Repository<JournalAudit>().AddAsync(new JournalAudit
            {
                DateUtc = DateTime.UtcNow,
                Tables = audit.TargetTable,
                Actions = "SYNC_PAIE",
                Nouveaux = summary,
                Users = string.IsNullOrWhiteSpace(audit.UserLogin) ? "SYSTEME:OvertimeService" : audit.UserLogin,
                BaseSage = sageDatabase,
                BasePointeuse = pointeuseDatabase,
                Correlations = audit.Correlation,
                Source = audit.Source
            }, cancellationToken);
            await uow.SaveChangesAsync(cancellationToken);
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
