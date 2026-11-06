namespace SoftTime.Application.Abstractions;

public sealed record ExternalEmployeeEvent(long EmployeeId, string? Code, DateTime? Start, DateTime? End, bool? Matin, bool? ApresMidi);
