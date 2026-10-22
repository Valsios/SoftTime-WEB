namespace SoftTime.Domain.Models;

/// <summary>
/// Options d'appariement du mode simple. <see cref="ParDefaut"/> reproduit exactement le
/// comportement historique (premier → dernier pointage, sans dédoublonnage ni borne).
/// </summary>
public sealed record SimpleDayOptions(
    bool Strict = false,
    TimeSpan? DuplicateWindow = null,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null)
{
    /// <summary>Deux pointages séparés de moins d'une minute sont un doublon de badge.</summary>
    public static readonly TimeSpan DefaultDuplicateWindow = TimeSpan.FromSeconds(60);

    /// <summary>Comportement historique, utilisé quand <c>SIMPLE_STRICT</c> est désactivé.</summary>
    public static readonly SimpleDayOptions ParDefaut = new();

    /// <summary>Fenêtre de dédoublonnage effective.</summary>
    public TimeSpan EffectiveDuplicateWindow => DuplicateWindow ?? DefaultDuplicateWindow;
}
