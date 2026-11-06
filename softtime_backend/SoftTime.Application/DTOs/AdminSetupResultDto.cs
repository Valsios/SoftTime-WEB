namespace SoftTime.Application.DTOs;

/// <summary>Résultat de la création du compte initial : indique si les identifiants ont pu être envoyés.</summary>
public record AdminSetupResultDto(decimal UserId, string Login, bool EmailEnvoye, string Message);
