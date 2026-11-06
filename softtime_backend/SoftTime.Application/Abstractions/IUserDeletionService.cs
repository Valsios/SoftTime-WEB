namespace SoftTime.Application.Abstractions;

public interface IUserDeletionService
{
    Task DeleteUserAsync(decimal currentUserId, decimal id, CancellationToken cancellationToken = default);
}
