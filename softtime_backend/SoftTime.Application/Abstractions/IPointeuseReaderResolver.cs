using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions;

/// <summary>Contrat de <c>PointeuseReaderResolver</c>.</summary>
public interface IPointeuseReaderResolver
{
    IPointeuseReader Resolve(BddPointeuse pteRow);
}
