namespace SoftTime.Application.Abstractions;

public interface ICurrentUser
{
    decimal? UserId { get; }
    string? Login { get; }
    decimal? RoleId { get; }
    string? Matricule { get; }
    IReadOnlyCollection<int> Rights { get; }
    bool HasRight(int droit);
}
