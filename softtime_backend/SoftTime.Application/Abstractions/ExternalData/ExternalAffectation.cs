namespace SoftTime.Application.Abstractions.ExternalData;

public sealed record ExternalAffectation(long EmployeeId, string? Departement, string? Service, DateTime? DateDebut, DateTime? DateSortie);
