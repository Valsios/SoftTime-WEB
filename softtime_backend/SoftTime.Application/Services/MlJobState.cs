namespace SoftTime.Application.Services;

/// <summary>État exposé par <c>GET /api/predictions/status</c>.</summary>
public sealed record MlJobState(
    bool Running,
    string Message,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    string? LastError);
