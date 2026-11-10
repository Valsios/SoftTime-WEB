using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Users;

public interface IDatabaseAccessService
{
    Task<IReadOnlyList<DbAccessDto>> GetDbAccessAsync(decimal? userId, CancellationToken cancellationToken = default);

    Task SetDbAccessAsync(decimal userId, IReadOnlyCollection<int> sageDbIds, CancellationToken cancellationToken = default);
}
