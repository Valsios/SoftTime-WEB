namespace SoftTime.Application.Abstractions.Ml;

public interface IMlDatasetContextLoader
{
    Task<DatasetContext> LoadContextAsync(DateTime from, DateTime to, CancellationToken ct);
}
