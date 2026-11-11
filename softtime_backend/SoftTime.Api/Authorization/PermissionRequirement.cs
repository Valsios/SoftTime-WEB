using Microsoft.AspNetCore.Authorization;

namespace SoftTime.Api.Authorization;

/// <summary>Exigence d'autorisation : détenir au moins une des permissions fines.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(IReadOnlyList<string> anyOf) => AnyOf = anyOf;

    public IReadOnlyList<string> AnyOf { get; }
}
