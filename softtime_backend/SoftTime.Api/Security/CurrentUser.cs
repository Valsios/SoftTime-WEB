using SoftTime.Application.Abstractions.RequestContext;

namespace SoftTime.Api.Security;

public class CurrentUser : ICurrentUser
{
    public decimal? UserId { get; init; }
    public string? Login { get; init; }
    public decimal? RoleId { get; init; }
    public string? Matricule { get; init; }
    public IReadOnlyCollection<int> Rights { get; init; } = Array.Empty<int>();
    public bool HasRight(int droit) => RoleId == SoftTime.Domain.Services.AdminRules.AdminRoleId || Rights.Contains(droit);
}
