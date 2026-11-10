using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services.Payroll;

public sealed class PayrollWriterResolver : IPayrollWriterResolver
{
    private readonly IReadOnlyList<IPayrollWriter> _writers;

    public PayrollWriterResolver(IEnumerable<IPayrollWriter> writers) => _writers = writers.ToList();

    public IPayrollWriter Resolve(BddSage database) => _writers.SingleOrDefault(w => w.CanHandle(database))
        ?? throw new DomainException($"Aucun writer de paie n'est configuré pour le type de base '{database.TypeBase}'.");
}
