namespace SoftTime.Application.Abstractions.RequestContext;

/// <summary>
/// Marque un scope créé par un service d'arrière-plan : aucune requête HTTP,
/// donc aucun utilisateur. Le job a été autorisé à sa demande (endpoint administrateur) ou relève
/// du cycle planifié ; le scope s'exécute alors sous une identité système administratrice.
/// Jamais activé pendant une requête HTTP.
/// </summary>
public sealed class SystemExecution
{
    public const string Login = "system:ml";

    public bool Active { get; private set; }

    public void Enable() => Active = true;
}
