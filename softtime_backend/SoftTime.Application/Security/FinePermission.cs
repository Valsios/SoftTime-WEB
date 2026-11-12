namespace SoftTime.Application.Security;

/// <summary>Permission fine exigée par une opération, avec la porte qui la porte (ex. 3 et « PROC:SHIFTS:DELETE »).</summary>
public sealed record FinePermission(int Gate, string Code);
