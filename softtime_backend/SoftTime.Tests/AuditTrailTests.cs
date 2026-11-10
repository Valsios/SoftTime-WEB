using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Audit;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Tests;

public class AuditTrailTests
{
    [Fact]
    public async Task WritePayrollSyncAsync_Success_WritesNonSensitiveSummaryWithCounts()
    {
        // Arrange
        var uow = new InMemoryUnitOfWork();
        var sut = new AuditTrail(new ScopeFactory(uow), new TestCompanyContext(), Options.Create(new AuditJournalOptions()), new RecordingLogger<AuditTrail>());

        // Act
        await sut.WritePayrollSyncAsync(Audit("SUCCES", new Dictionary<string, int>
        {
            ["Exo130"] = 2,
            ["Nuit"] = 2
        }));

        // Assert
        var entry = uow.Get<JournalAudit>().Items.Should().ContainSingle().Which;
        entry.Tables.Should().Be("T_CUMSAL");
        entry.Actions.Should().Be("SYNC_PAIE");
        entry.Users.Should().Be("admin");
        entry.BaseSage.Should().Be("SAGE_TEST");
        entry.Source.Should().Be("SYNC_PAIE:STANDARD");
        entry.Nouveaux.Should().Contain("Statut=SUCCES");
        entry.Nouveaux.Should().Contain("LignesMisesÀJour=4");
        entry.Nouveaux.Should().Contain("Exo130=2");
        entry.Nouveaux.Should().NotContain("123.45");
        entry.Nouveaux.Should().NotContain("Dupont");
    }

    [Fact]
    public async Task WritePayrollSyncAsync_Failure_WritesSummaryWithoutIndividualData()
    {
        // Arrange
        var uow = new InMemoryUnitOfWork();
        var sut = new AuditTrail(new ScopeFactory(uow), new TestCompanyContext(), Options.Create(new AuditJournalOptions()), new RecordingLogger<AuditTrail>());

        // Act
        await sut.WritePayrollSyncAsync(Audit("ECHEC", new Dictionary<string, int> { ["Exo130"] = 3 }));

        // Assert
        var entry = uow.Get<JournalAudit>().Items.Should().ContainSingle().Which;
        entry.Nouveaux.Should().Contain("Statut=ECHEC");
        entry.Nouveaux.Should().Contain("LignesMisesÀJour=3");
        entry.Nouveaux.Should().NotContainEquivalentOf("matricule");
    }

    [Fact]
    public async Task WritePayrollSyncAsync_AuditDisabled_WritesNothing()
    {
        // Arrange
        var uow = new InMemoryUnitOfWork();
        var sut = new AuditTrail(new ScopeFactory(uow), new TestCompanyContext(), Options.Create(new AuditJournalOptions { Enabled = false }), new RecordingLogger<AuditTrail>());

        // Act
        await sut.WritePayrollSyncAsync(Audit("SUCCES", new Dictionary<string, int>()));

        // Assert
        uow.Get<JournalAudit>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task WritePayrollSyncAsync_JournalFails_LogsAndDoesNotThrow()
    {
        // Arrange
        var logger = new RecordingLogger<AuditTrail>();
        var trail = new AuditTrail(new ScopeFactory(new FailingUnitOfWork()), new TestCompanyContext(), Options.Create(new AuditJournalOptions()), logger);

        // Act
        var exception = await Record.ExceptionAsync(() => trail.WritePayrollSyncAsync(Audit("SUCCES", new Dictionary<string, int>())));

        // Assert
        exception.Should().BeNull();
        logger.Messages.Should().Contain(message => message.Contains("Impossible d'écrire la synthèse", StringComparison.Ordinal));
    }

    private static PayrollSyncAudit Audit(string status, IReadOnlyDictionary<string, int> categories)
        => new("T_CUMSAL", "SAGE_TEST", new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), 2,
            categories.Values.Sum(), 0, categories, status, "admin", "SYNC_PAIE:STANDARD", Guid.NewGuid());

    [Fact]
    public async Task WritePayrollSyncAsync_NoSageDatabase_WritesCompanyDatabases()
    {
        // Arrange
        var uow = new InMemoryUnitOfWork();
        var company = new TestCompanyContext("SAGE_COURANTE", "POINTEUSE_COURANTE");
        var sut = new AuditTrail(new ScopeFactory(uow), company, Options.Create(new AuditJournalOptions()), new RecordingLogger<AuditTrail>());
        var audit = Audit("SUCCES", new Dictionary<string, int>()) with { SageDatabase = null };

        // Act
        await sut.WritePayrollSyncAsync(audit);

        // Assert
        var entry = uow.Get<JournalAudit>().Items.Should().ContainSingle().Which;
        entry.BaseSage.Should().Be("SAGE_COURANTE");
        entry.BasePointeuse.Should().Be("POINTEUSE_COURANTE");
    }

    [Fact]
    public async Task WriteAsync_Success_WritesSummaryWithExactCounts()
    {
        // Arrange
        var audit = new CapturingAuditTrail();
        var sut = new TestStandardWriter(new SuccessfulSageWriter(), audit);

        // Act
        await sut.WriteAsync(Database(), Rows(), Context());

        // Assert
        var summary = audit.Entries.Should().ContainSingle().Which;
        summary.Status.Should().Be("SUCCES");
        summary.UpdatedRows.Should().Be(7);
        summary.Employees.Should().Be(1);
        summary.UpdatesByCategory.Values.Should().AllSatisfy(value => value.Should().Be(1));
    }

    [Fact]
    public async Task WriteAsync_PartialFailure_WritesExactCountsAndRethrows()
    {
        // Arrange
        var original = new InvalidOperationException("missing operand");
        var audit = new CapturingAuditTrail();
        var sut = new TestStandardWriter(new FailingSageWriter(original, 3), audit);

        // Act
        Func<Task> act = () => sut.WriteAsync(Database(), Rows(), Context());

        // Assert
        var thrown = (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).Which;
        thrown.Should().BeSameAs(original);
        var summary = audit.Entries.Should().ContainSingle().Which;
        summary.Status.Should().Be("ECHEC");
        summary.UpdatedRows.Should().Be(3);
    }

    [Fact]
    public async Task WriteAsync_CancelledRequest_StillWritesFailure()
    {
        // Arrange
        var audit = new CapturingAuditTrail();
        var sut = new TestStandardWriter(new CancelledSageWriter(), audit);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        Func<Task> act = () => sut.WriteAsync(Database(), Rows(), Context(), cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        audit.Entries.Should().ContainSingle().Which.Status.Should().Be("ECHEC");
        audit.Tokens.Single().Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task WriteAsync_JournalWriteFails_KeepsOriginalOutcome()
    {
        // Arrange
        var logger = new RecordingLogger<AuditTrail>();
        var audit = new AuditTrail(new ScopeFactory(new FailingUnitOfWork()), new TestCompanyContext(), Options.Create(new AuditJournalOptions()), logger);
        var sut = new TestStandardWriter(new SuccessfulSageWriter(), audit);

        // Act
        var result = await sut.WriteAsync(Database(), Rows(), Context());

        // Assert
        result.EmployeesUpdated.Should().Be(1);
        result.CategoriesUpdated.Should().Be(7);
        logger.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task WriteAsync_MissingSageConstant_RecordsZeroUpdates()
    {
        // Arrange
        var audit = new CapturingAuditTrail();
        var sut = new TestStandardWriter(new SagePayrollWriter(), audit);

        // Act
        Func<Task> act = () => sut.WriteAsync(Database(), Rows(), Context());

        // Assert
        var exception = (await act.Should().ThrowExactlyAsync<DomainException>()).Which;
        exception.Message.Should().Contain("Constante SAGE HS01 introuvable");
        var summary = audit.Entries.Should().ContainSingle().Which;
        summary.Status.Should().Be("ECHEC");
        summary.UpdatedRows.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_CancelledAfterPayroll_StillWritesSuccess()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var audit = new CapturingAuditTrail();
        var sut = new TestStandardWriter(new CancellingSageWriter(cancellation), audit);

        // Act
        await sut.WriteAsync(Database(), Rows(), Context(), cancellation.Token);

        // Assert
        audit.Entries.Should().ContainSingle().Which.Status.Should().Be("SUCCES");
        audit.Tokens.Single().Should().Be(CancellationToken.None);
    }

    private static BddSage Database() => new() { NomBd = "SAGE_TEST", TypeBase = "STANDARD" };
    private static IReadOnlyList<HsExoDto> Rows() => [new HsExoDto(1, null, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0)];
    private static PayrollSyncContext Context() => new(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), "admin", Guid.NewGuid());

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        private readonly InMemoryRepository<JournalAudit> _journal = new();

        public IRepository<T> Repository<T>() where T : class
            => typeof(T) == typeof(JournalAudit)
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

        protected override Task<SagePayrollTarget> GetInputAsync(CancellationToken ct)
            => Task.FromResult(new SagePayrollTarget("test", [], new OvertimeSageCodes()));
    }

    private sealed class SuccessfulSageWriter : ISagePayrollWriter
    {
        public Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(SagePayrollTarget target, int number, OvertimeAmounts amounts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, int>>(PayrollWritingConfiguration.Categories.ToDictionary(category => category, _ => 1));
        public Task<int> UpdateCumulAsync(string connection, int number, short operand, decimal value, CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FailingSageWriter(Exception exception, int completed) : ISagePayrollWriter
    {
        public Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(SagePayrollTarget target, int number, OvertimeAmounts amounts, CancellationToken ct = default)
        {
            var categories = PayrollWritingConfiguration.Categories.Take(completed).ToDictionary(category => category, _ => 1);
            throw new PayrollSyncWriteException(exception, completed, categories);
        }
        public Task<int> UpdateCumulAsync(string connection, int number, short operand, decimal value, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class CancelledSageWriter : ISagePayrollWriter
    {
        public Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(SagePayrollTarget target, int number, OvertimeAmounts amounts, CancellationToken ct = default)
            => throw new OperationCanceledException(ct);
        public Task<int> UpdateCumulAsync(string connection, int number, short operand, decimal value, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class CancellingSageWriter(CancellationTokenSource cancellation) : ISagePayrollWriter
    {
        public Task<IReadOnlyDictionary<string, int>> SyncOvertimeAsync(SagePayrollTarget target, int number, OvertimeAmounts amounts, CancellationToken ct = default)
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyDictionary<string, int>>(PayrollWritingConfiguration.Categories.ToDictionary(category => category, _ => 1));
        }
        public Task<int> UpdateCumulAsync(string connection, int number, short operand, decimal value, CancellationToken ct = default) => Task.FromResult(1);
    }
}
