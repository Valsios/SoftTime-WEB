namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Connexion saisie pour une base externe (SAGE ou POINTEUSE) ; <c>Id</c> désigne la base enregistrée
/// dont le mot de passe peut être réutilisé quand il n'est pas ressaisi.
/// </summary>
public sealed record ConnectionAttempt(string? Kind, int? Id, string? Serveur, bool? SqlAuth, string? Login, string? Password);
