using Microsoft.Extensions.Logging;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Tests;

public class AuditTrailTests
{
    [Fact]
    public async Task Writes_a_non_sensitive_payroll_summary_with_counts()
    {
        var uow = new InMemoryUnitOfWork();
        var trail = new AuditTrail(uow, new FakeCompanyContext(), new AuditJournalOptions(), new RecordingLogger<AuditTrail>());

        await trail.WritePayrollSyncAsync(Audit("SUCCES", new Dictionary<string, int>
        {
            ["Exo130"] = 2,
            ["Nuit"] = 2
        }));

        var entry = Assert.Single(uow.Get<suivi_JOURNAL>().Items);
        Assert.Equal("T_CUMSAL", entry.TABLES);
        Assert.Equal("SYNC_PAIE", entry.ACTIONS);
        Assert.Equal("admin", entry.USERS);
        Assert.Equal("SAGE_TEST", entry.BASE_SAGE);
        Assert.Equal("SYNC_PAIE:STANDARD", entry.SOURCE);
        Assert.Contains("Statut=SUCCES", entry.NOUVEAUX);
        Assert.Contains("LignesMisesÀJour=4", entry.NOUVEAUX);
        Assert.Contains("Exo130=2", entry.NOUVEAUX);
        Assert.DoesNotContain("123.45", entry.NOUVEAUX);
        Assert.DoesNotContain("Dupont", entry.NOUVEAUX);
    }

    [Fact]
    public async Task Writes_failure_summary_without_individual_data()
    {
        var uow = new InMemoryUnitOfWork();
        var trail = new AuditTrail(uow, new FakeCompanyContext(), new AuditJournalOptions(), new RecordingLogger<AuditTrail>());

        await trail.WritePayrollSyncAsync(Audit("ECHEC", new Dictionary<string, int> { ["Exo130"] = 3 }));

        var entry = Assert.Single(uow.Get<suivi_JOURNAL>().Items);
        Assert.Contains("Statut=ECHEC", entry.NOUVEAUX);
        Assert.Contains("LignesMisesÀJour=3", entry.NOUVEAUX);
        Assert.DoesNotContain("matricule", entry.NOUVEAUX, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabled_audit_writes_nothing()
    {
        var uow = new InMemoryUnitOfWork();
        var trail = new AuditTrail(uow, new FakeCompanyContext(), new AuditJournalOptions { Enabled = false }, new RecordingLogger<AuditTrail>());

        await trail.WritePayrollSyncAsync(Audit("SUCCES", new Dictionary<string, int>()));

        Assert.Empty(uow.Get<suivi_JOURNAL>().Items);
    }

    [Fact]
    public async Task Journal_failure_is_logged_and_does_not_throw()
    {
        var logger = new RecordingLogger<AuditTrail>();
        var trail = new AuditTrail(new FailingUnitOfWork(), new FakeCompanyContext(), new AuditJournalOptions(), logger);

        var exception = await Record.ExceptionAsync(() => trail.WritePayrollSyncAsync(Audit("SUCCES", new Dictionary<string, int>())));

        Assert.Null(exception);
        Assert.Contains(logger.Messages, message => message.Contains("Impossible d'écrire la synthèse", StringComparison.Ordinal));
    }

    private static PayrollSyncAudit Audit(string status, IReadOnlyDictionary<string, int> categories)
        => new("T_CUMSAL", "SAGE_TEST", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
            categories.Values.Sum(), categories, status, "admin", "SYNC_PAIE:STANDARD", Guid.NewGuid());

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        private readonly InMemoryRepository<suivi_JOURNAL> _journal = new();

        public IRepository<T> Repository<T>() where T : class
            => typeof(T) == typeof(suivi_JOURNAL)
                ? (IRepository<T>)(object)_journal
                : throw new InvalidOperationException("Unexpected repository.");

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromException<int>(new InvalidOperationException("journal unavailable"));

        public Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
