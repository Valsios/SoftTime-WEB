using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests;

public class PayrollWriterResolverTests
{
    [Theory]
    [InlineData("STANDARD", "STANDARD")]
    [InlineData("AUTRE", "AUTRE")]
    public void SelectsWriterForConfiguredDatabaseType(string typeBase, string expected)
    {
        var resolver = new PayrollWriterResolver(new IPayrollWriter[] { new StubWriter("STANDARD"), new StubWriter("AUTRE") });
        var writer = resolver.Resolve(new T_BDD_SAGE { TYPE_BASE = typeBase });
        Assert.Equal(expected, ((StubWriter)writer).Type);
    }

    private sealed class StubWriter(string type) : IPayrollWriter
    {
        public string Type { get; } = type;
        public bool CanHandle(T_BDD_SAGE database) => string.Equals(database.TYPE_BASE, Type, StringComparison.OrdinalIgnoreCase);
        public Task<PayrollWriteResultDto> ValidateAsync(T_BDD_SAGE database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
            => Task.FromResult(new PayrollWriteResultDto("", Type, 0, 0, ""));
        public Task<PayrollWriteResultDto> WriteAsync(T_BDD_SAGE database, IReadOnlyList<HsExoDto> rows, CancellationToken ct = default)
            => Task.FromResult(new PayrollWriteResultDto("", Type, 0, 0, ""));
    }
}
