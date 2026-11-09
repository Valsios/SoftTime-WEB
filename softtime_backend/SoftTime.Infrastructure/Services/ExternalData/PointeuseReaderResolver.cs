using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services.ExternalData;

public class PointeuseReaderResolver : IPointeuseReaderResolver
{
    private readonly IEnumerable<IPointeuseReader> _readers;
    public PointeuseReaderResolver(IEnumerable<IPointeuseReader> readers) => _readers = readers;

    public IPointeuseReader Resolve(BddPointeuse pteRow)
        => _readers.FirstOrDefault(r => r.CanHandle(pteRow))
           ?? throw new DomainException($"Aucun lecteur pointeuse ne gère le type de base '{pteRow.TypeBase}'.");
}
