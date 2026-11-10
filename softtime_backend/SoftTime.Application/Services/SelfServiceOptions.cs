namespace SoftTime.Application.Services;

/// <summary>
/// Réglage de « Mon espace » : désactivé par défaut. Section « SelfService », clé « Enabled »
/// (variable d'environnement SelfService__Enabled). Désactivé, toutes les routes répondent 404.
/// </summary>
public sealed class SelfServiceOptions
{
    public const string Section = "SelfService";

    public bool Enabled { get; set; }
}
