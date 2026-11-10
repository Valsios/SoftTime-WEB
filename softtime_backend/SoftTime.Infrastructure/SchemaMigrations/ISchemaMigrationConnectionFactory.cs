namespace SoftTime.Infrastructure.SchemaMigrations;

public interface ISchemaMigrationConnectionFactory
{
    Task<ISchemaMigrationConnection> OpenAsync(CancellationToken ct);
}
