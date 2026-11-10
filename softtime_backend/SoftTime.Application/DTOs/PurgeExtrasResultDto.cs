namespace SoftTime.Application.DTOs;

/// <summary>Résultat de la purge des lignes cantine / frais d'une période.</summary>
public record PurgeExtrasResultDto(int LignesSupprimees, string Message);
