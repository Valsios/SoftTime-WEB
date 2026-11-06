namespace SoftTime.Application.DTOs;

/// <summary>Résultat de la synchronisation des cumuls cantine / frais vers SAGE.</summary>
public record ExtrasSyncResultDto(string Database, string TypeBase, int EmployeesSynced, int CumulsUpdated, string Message);
