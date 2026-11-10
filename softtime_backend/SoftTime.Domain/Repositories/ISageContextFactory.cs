namespace SoftTime.Domain.Repositories;

public interface ISageContextFactory
{
    IDisposableSageContext Create(string connectionString);
}
