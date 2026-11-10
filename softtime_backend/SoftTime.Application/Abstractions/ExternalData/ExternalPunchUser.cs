namespace SoftTime.Application.Abstractions.ExternalData;

public sealed record ExternalPunchUser(long UserId, string? Badge, string? Ssn, string? Name);
