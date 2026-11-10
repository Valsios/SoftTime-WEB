using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

public class PayrollWriterResolverTests
{
    [Theory]
    [InlineData("STANDARD", "STANDARD")]
    [InlineData("AUTRE", "AUTRE")]
    public void Resolve_ConfiguredDatabaseType_SelectsMatchingWriter(string typeBase, string expected)
    {
        // Arrange
        var sut = new PayrollWriterResolver(new IPayrollWriter[] { new StubWriter("STANDARD"), new StubWriter("AUTRE") });

        // Act
        var writer = sut.Resolve(new BddSage { TypeBase = typeBase });

        // Assert
        ((StubWriter)writer).Type.Should().Be(expected);
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
