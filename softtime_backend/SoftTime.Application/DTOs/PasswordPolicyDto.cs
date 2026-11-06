namespace SoftTime.Application.DTOs;

/// <summary>
/// Politique de mot de passe exposée au client : longueur minimale et règles affichables,
/// pour que l'interface valide exactement comme le serveur.
/// </summary>
public record PasswordPolicyDto(int MinLength, IReadOnlyList<PasswordPolicyRuleDto> Rules);
