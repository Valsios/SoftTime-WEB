using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Tests;

public class AuditTrailTests
{
    [Fact]
    public async Task Writes_a_non_sensitive_payroll_summary_with_counts()
    {
        var uow = new InMemoryUnitOfWork();
        var trail = new AuditTrail(new ScopeFactory(uow), new TestCompanyContext(), new AuditJournalOptions(), new RecordingLogger<AuditTrail>());

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
        var trail = new AuditTrail(new ScopeFactory(uow), new TestCompanyContext(), new AuditJournalOptions(), new RecordingLogger<AuditTrail>());

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
        var trail = new AuditTrail(new ScopeFactory(uow), new TestCompanyContext(), new AuditJournalOptions { Enabled = false }, new RecordingLogger<AuditTrail>());

        await trail.WritePayrollSyncAsync(Audit("SUCCES", new Dictionary<string, int>()));

        Assert.Empty(uow.Get<suivi_JOURNAL>().Items);
    }

    [Fact]
    public async Task Journal_failure_is_logged_and_does_not_throw()
    {
        var logger = new RecordingLogger<AuditTrail>();
        var trail = new AuditTrail(new ScopeFactory(new FailingUnitOfWork()), new TestCompanyContext(), new AuditJournalOptions(), logger);

        var exception = await Record.ExceptionAsync(() => trail.WritePayrollSyncAsync(Audit("SUCCES", new Dictionary<string, int>())));

        Assert.Null(exception);
        Assert.Contains(logger.Messages, message => message.Contains("Impossible d'écrire la synthèse", StringComparison.Ordinal));
    }

    private static PayrollSyncAudit Audit(string status, IReadOnlyDictionary<string, int> categories)
        => new("T_CUMSAL", "SAGE_TEST", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), 2,
            categories.Values.Sum(), 0, categories, status, "admin", "SYNC_PAIE:STANDARD", Guid.NewGuid());

    [Fact]
    public async Task Writes_company_databases_when_the_audit_has_no_sage_database()
    {
        var uow = new InMemoryUnitOfWork();
        var company = new TestCompanyContext("SAGE_COURANTE", "POINTEUSE_COURANTE");
        var trail = new AuditTrail(new ScopeFactory(uow), company, new AuditJournalOptions(), new RecordingLogger<AuditTrail>());
        var audit = Audit("SUCCES", new Dictionary<string, int>()) with { SageDatabase = null };

        await trail.WritePayrollSyncAsync(audit);

        var entry = Assert.Single(uow.Get<suivi_JOURNAL>().Items);
        Assert.Equal("SAGE_COURANTE", entry.BASE_SAGE);
        Assert.Equal("POINTEUSE_COURANTE", entry.BASE_POINTEUSE);
    }

    [Fact]
    public async Task Standard_writer_writes_success_summary_with_exact_counts()
    {
        var audit = new CapturingAuditTrail();
        var writer = new TestStandardWriter(new SuccessfulSageWriter(), audit);

        await writer.WriteAsync(Database(), Rows(), Context());

        var summary = Assert.Single(audit.Entries);
        Assert.Equal("SUCCES", summary.Status);
        Assert.Equal(7, summary.UpdatedRows);
        Assert.Equal(1, summary.Employees);
        Assert.All(summary.UpdatesByCategory.Values, value => Assert.Equal(1, value));
    }

    [Fact]
    public async Task Standard_writer_writes_exact_partial_failure_and_rethrows_original_exception()
    {
        var original = new InvalidOperationException("missing operand");
        var audit = new CapturingAuditTrail();
        var writer = new TestStandardWriter(new FailingSageWriter(original, 3), audit);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(Database(), Rows(), Context()));

        Assert.Same(original, thrown);
        var summary = Assert.Single(audit.Entries);
        Assert.Equal("ECHEC", summary.Status);
        Assert.Equal(3, summary.UpdatedRows);
    }

    [Fact]
    public async Task Standard_writer_writes_failure_even_when_request_is_cancelled()
    {
        var audit = new CapturingAuditTrail();
        var writer = new TestStandardWriter(new CancelledSageWriter(), audit);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(Database(), Rows(), Context(), cancellation.Token));

        Assert.Equal("ECHEC", Assert.Single(audit.Entries).Status);
        Assert.Equal(CancellationToken.None, audit.Tokens.Single());
    }

    [Fact]
    public async Task Standard_writer_is_not_masked_when_the_journal_write_fails()
    {
        var logger = new RecordingLogger<AuditTrail>();
        var audit = new AuditTrail(new ScopeFactory(new FailingUnitOfWork()), new TestCompanyContext(), new AuditJournalOptions(), logger);
        var writer = new TestStandardWriter(new SuccessfulSageWriter(), audit);

        var result = await writer.WriteAsync(Database(), Rows(), Context());

        Assert.Equal(1, result.EmployeesUpdated);
        Assert.Equal(7, result.CategoriesUpdated);
        Assert.NotEmpty(logger.Messages);
    }

    [Fact]
    public async Task Standard_writer_records_zero_updates_when_a_sage_constant_is_missing()
    {
        var audit = new CapturingAuditTrail();
        var writer = new TestStandardWriter(new SagePayrollWriter(), audit);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(Database(), Rows(), Context()));

        Assert.Contains("Constante SAGE HS01 introuvable", exception.Message);
        var summary = Assert.Single(audit.Entries);
        Assert.Equal("ECHEC", summary.Status);
        Assert.Equal(0, summary.UpdatedRows);
    }

    [Fact]
    public async Task Standard_writer_writes_success_even_if_the_request_is_cancelled_after_payroll()
    {
        using var cancellation = new CancellationTokenSource();
        var audit = new CapturingAuditTrail();
        var writer = new TestStandardWriter(new CancellingSageWriter(cancellation), audit);

        await writer.WriteAsync(Database(), Rows(), Context(), cancellation.Token);

        Assert.Equal("SUCCES", Assert.Single(audit.Entries).Status);
        Assert.Equal(CancellationToken.None, audit.Tokens.Single());
    }

    private static T_BDD_SAGE Database() => new() { NOM_BD = "SAGE_TEST", TYPE_BASE = "STANDARD" };
    private static IReadOnlyList<HsExoDto> Rows() => [new HsExoDto(1, null, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0)];
    private static PayrollSyncContext Context() => new(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), "admin", Guid.NewGuid());

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

    private sealed class ScopeFactory(IUnitOfWork uow) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(uow);
        private sealed class Scope(IUnitOfWork uow) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => serviceType == typeof(IUnitOfWork) ? uow : null;
            public void Dispose() { }
        }
    }

    private sealed class TestCompanyContext(string? sageDatabase = null, string? pointeuseDatabase = null) : ICompanyContext
    {
        public string? SageDatabase { get; } = sageDatabase;
        public string? PointeuseDatabase { get; } = pointeuseDatabase;
        public void Set(string sage, string? pointeuse) { }
    }

    private sealed class CapturingAuditTrail : IAuditTrail
    {
        public List<PayrollSyncAudit> Entries { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default)
        {
            Entries.Add(audit);
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class TestStandardWriter : StandardPayrollWriter
    {
        public TestStandardWriter(ISagePayrollWriter sageWriter, IAuditTrail auditTrail)
            : base(sageWriter, null!, null!, null!, auditTrail) { }

        protected override Task<StandardPayrollInput> GetInputAsync(CancellationToken ct)
            => Task.FromResult(new StandardPayrollInput("test", [], new OvertimeSageCodes()));
    }

    private sealed class SuccessfulSageWriter : ISagePayrollWriter
    {
        public Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(string connection, T_CST[] constants, int number, OvertimeSageCodes codes, decimal exo130, decimal exo150, decimal i130, decimal i150, decimal ferie, decimal nuit, decimal dim, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, int>>(PayrollWritingConfiguration.Categories.ToDictionary(category => category, _ => 1));
        public Task<int> UpdateCumulAsync(string connection, int number, short operand, decimal value, CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FailingSageWriter(Exception exception, int completed) : ISagePayrollWriter
    {
        public Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(string connection, T_CST[] constants, int number, OvertimeSageCodes codes, decimal exo130, decimal exo150, decimal i130, decimal i150, decimal ferie, decimal nuit, decimal dim, CancellationToken ct = default)
        {
            var categories = PayrollWritingConfiguration.Categories.Take(completed).ToDictionary(category => category, _ => 1);
            throw new PayrollSyncWriteException(exception, completed, categories);
        }
        public Task<int> UpdateCumulAsync(string connection, int number, short operand, decimal value, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class CancelledSageWriter : ISagePayrollWriter
    {
        public Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(string connection, T_CST[] constants, int number, OvertimeSageCodes codes, decimal exo130, decimal exo150, decimal i130, decimal i150, decimal ferie, decimal nuit, decimal dim, CancellationToken ct = default)
            => throw new OperationCanceledException(ct);
        public Task<int> UpdateCumulAsync(string connection, int number, short operand, decimal value, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class CancellingSageWriter(CancellationTokenSource cancellation) : ISagePayrollWriter
    {
        public Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(string connection, T_CST[] constants, int number, OvertimeSageCodes codes, decimal exo130, decimal exo150, decimal i130, decimal i150, decimal ferie, decimal nuit, decimal dim, CancellationToken ct = default)
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyDictionary<string, int>>(PayrollWritingConfiguration.Categories.ToDictionary(category => category, _ => 1));
        }
        public Task<int> UpdateCumulAsync(string connection, int number, short operand, decimal value, CancellationToken ct = default) => Task.FromResult(1);
    }
}
