namespace SoftTime.Infrastructure.Services;

public interface IMlDatasetContextLoader
{
    Task<DatasetContext> LoadContextAsync(DateTime from, DateTime to, CancellationToken ct);
}
