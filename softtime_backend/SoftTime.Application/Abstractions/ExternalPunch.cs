namespace SoftTime.Application.Abstractions;

public sealed record ExternalPunch(long UserId, DateTime CheckTime, string? CheckType);
