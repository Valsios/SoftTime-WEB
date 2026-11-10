namespace SoftTime.Application.Security;

/// <summary>Incohérence signalée sans blocage (par exemple une porte de travail sans accès à une base).</summary>
public sealed record RightsInconsistency(string Code, int Gate, string Message);
