namespace SoftTime.Application.Abstractions;

public interface ICompanyContext
{
    string? SageDatabase { get; }
    string? PointeuseDatabase { get; }
    void Set(string sage, string? pointeuse);
}

/// <summary>
/// Marque un scope créé par un service d'arrière-plan (moteur prédictif) : aucune requête HTTP,
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

public interface ICurrentUser
{
    decimal? UserId { get; }
    string? Login { get; }
    decimal? RoleId { get; }
    string? Matricule { get; }
    IReadOnlyCollection<int> Rights { get; }
    bool HasRight(int droit);
}
