namespace SoftTime.Application.Abstractions;

public sealed record ExternalPunchUser(long UserId, string? Badge, string? Ssn, string? Name);
