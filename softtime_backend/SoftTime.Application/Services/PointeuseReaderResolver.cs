using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Services;

public class PointeuseReaderResolver
{
    private readonly IEnumerable<IPointeuseReader> _readers;
    public PointeuseReaderResolver(IEnumerable<IPointeuseReader> readers) => _readers = readers;

    public IPointeuseReader Resolve(T_BDD_POINTEUSE pteRow)
        => _readers.FirstOrDefault(r => r.CanHandle(pteRow))
           ?? throw new InvalidOperationException($"Aucun lecteur pointeuse ne gère le type de base '{pteRow.TYPE_BASE}'.");
}