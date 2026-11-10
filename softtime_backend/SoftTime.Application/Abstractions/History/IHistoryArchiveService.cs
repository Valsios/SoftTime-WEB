using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Abstractions.History;

public interface IHistoryArchiveService
{
    Task ArchivePunchesAsync(IReadOnlyCollection<Pointage> rows, CancellationToken ct = default);

    Task ArchiveAnomaliesAsync(IReadOnlyCollection<HeureAnomalie> rows, CancellationToken ct = default);

    Task ArchiveHsAsync(IReadOnlyCollection<HsExoImp> rows, CancellationToken ct = default);
}
