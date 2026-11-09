using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions.Permissions;

public interface IPrivilegeService
{
    Task<IReadOnlyList<DroitDto>> GetDroitsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PrivilegeDto>> GetPrivilegesAsync(decimal? roleId, CancellationToken cancellationToken = default);

    Task SetPrivilegesAsync(decimal roleId, IReadOnlyCollection<decimal> droitIds, CancellationToken cancellationToken = default);
}
