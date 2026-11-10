namespace SoftTime.Domain.Repositories;

public interface IPointeuseContextFactory
{
    IDisposablePointeuseContext Create(string connectionString);
}
