namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>
/// Durées de vie d'une entrée du cache de clôture : délai <c>Glissant</c>, renouvelé à chaque lecture avec
/// réutilisation, sous un <c>Plafond</c> absolu mesuré depuis le calcul d'origine. Le délai glissant ne dépasse jamais
/// le plafond.
/// </summary>
internal readonly record struct ClosingCacheWindow(TimeSpan Glissant, TimeSpan Plafond)
{
    /// <summary>Fenêtre des réglages ; un plafond à 0 fait expirer l'analyse après le seul délai glissant.</summary>
    public static ClosingCacheWindow Depuis(int cacheSeconds, int cacheMaxSeconds) => new(
        TimeSpan.FromSeconds(cacheSeconds),
        TimeSpan.FromSeconds(cacheMaxSeconds > 0 ? cacheMaxSeconds : cacheSeconds));
}
