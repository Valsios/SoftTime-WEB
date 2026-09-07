namespace SoftTime.Domain.Repositories;

public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default);
}
