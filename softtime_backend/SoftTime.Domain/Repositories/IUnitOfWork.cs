namespace SoftTime.Domain.Repositories;

public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exécute <paramref name="action"/> dans une transaction, sous un verrou exclusif nommé
    /// <paramref name="resource"/> partagé par toutes les instances de l'API (<c>sp_getapplock</c> sur SQL Server).
    /// Deux appels sur la même ressource s'exécutent l'un après l'autre : la vérification puis l'écriture
    /// faites dans l'action ne peuvent pas être entrelacées. La transaction est validée si l'action réussit,
    /// annulée sinon. Par défaut (doubles de test) : exécution directe, sans verrou.
    /// </summary>
    Task<TResult> RunExclusiveAsync<TResult>(
        string resource,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
        => action(cancellationToken);
}
