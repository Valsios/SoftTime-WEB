using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services;

public sealed class PayrollWriterResolver : IPayrollWriterResolver
{
    private readonly IReadOnlyList<IPayrollWriter> _writers;

    public PayrollWriterResolver(IEnumerable<IPayrollWriter> writers) => _writers = writers.ToList();

    public IPayrollWriter Resolve(T_BDD_SAGE database) => _writers.SingleOrDefault(w => w.CanHandle(database))
        ?? throw new DomainException($"Aucun writer de paie n'est configuré pour le type de base '{database.TYPE_BASE}'.");
}
