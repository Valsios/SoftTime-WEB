namespace SoftTime.Application.DTOs;

/// <summary>Une règle de la politique de mot de passe, avec son libellé prêt à afficher.</summary>
public record PasswordPolicyRuleDto(string Code, string Label);
