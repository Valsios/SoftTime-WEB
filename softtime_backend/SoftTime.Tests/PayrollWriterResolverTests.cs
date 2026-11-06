using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

public class PayrollWriterResolverTests
{
    [Theory]
    [InlineData("STANDARD", "STANDARD")]
    [InlineData("AUTRE", "AUTRE")]
    public void SelectsWriterForConfiguredDatabaseType(string typeBase, string expected)
    {
        var resolver = new PayrollWriterResolver(new IPayrollWriter[] { new StubWriter("STANDARD"), new StubWriter("AUTRE") });
        var writer = resolver.Resolve(new BddSage { TypeBase = typeBase });
        Assert.Equal(expected, ((StubWriter)writer).Type);
    }

    private sealed class StubWriter(string type) : IPayrollWriter
    {
        public string Type { get; } = type;
        public bool CanHandle(BddSage database) => string.Equals(database.TypeBase, Type, StringComparison.OrdinalIgnoreCase);
        public Task<PayrollWriteResultDto> ValidateAsync(BddSage database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
            => Task.FromResult(new PayrollWriteResultDto("", Type, 0, 0, ""));
        public Task<PayrollWriteResultDto> WriteAsync(BddSage database, IReadOnlyList<HsExoDto> rows, PayrollSyncContext auditContext, CancellationToken ct = default)
            => Task.FromResult(new PayrollWriteResultDto("", Type, 0, 0, ""));
    }
}
