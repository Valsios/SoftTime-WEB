using SoftTime.Application.DTOs;

namespace SoftTime.Application.Abstractions;

public interface IUserDirectoryService
{
    Task<UserDto> GetUserAsync(decimal id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<UserDto>> GetUsersPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken cancellationToken = default);
}
