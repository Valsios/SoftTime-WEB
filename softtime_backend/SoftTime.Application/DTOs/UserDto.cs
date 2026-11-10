namespace SoftTime.Application.DTOs;

public record UserDto(decimal Id, decimal RoleId, string Nom, string Login, string? Matricule, string? Password)
{
    /// <summary>Adresse d'envoi des identifiants (facultative).</summary>
    public string? Email { get; init; }

    /// <summary>
    /// Changement de rôle : à vrai, confirme l'effacement des ajustements personnels du compte (défaut false).
    /// Sans cette confirmation, un changement de rôle d'un compte qui a des ajustements est refusé (409) et rien n'est écrit.
    /// </summary>
    public bool ConfirmClearAdjustments { get; init; } = false;

    public UserDto() : this(default, default, default!, default!, default, default) { }
}
