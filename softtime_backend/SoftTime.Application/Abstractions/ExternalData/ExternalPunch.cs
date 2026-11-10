namespace SoftTime.Application.Abstractions.ExternalData;

public sealed record ExternalPunch(long UserId, DateTime CheckTime, string? CheckType);
