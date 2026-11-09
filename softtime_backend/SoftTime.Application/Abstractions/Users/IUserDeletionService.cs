namespace SoftTime.Application.Abstractions.Users;

public interface IUserDeletionService
{
    Task DeleteUserAsync(decimal currentUserId, decimal id, CancellationToken cancellationToken = default);
}
