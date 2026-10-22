namespace SoftTime.Domain.Services;

/// <summary>
/// Calcul du retard à l'entrée, partagé par la validation semaine, le tableau de bord et l'état
/// retards. Une valeur <c>RETARD</c> saisie reste prioritaire (elle n'est jamais écrasée).
/// </summary>
public static class RetardRules
{
    /// <summary>
    /// Retard en minutes : entrée moins (début planifié + tolérance). Retourne <c>null</c> quand
    /// l'entrée ou le planning manquent, et <see cref="TimeSpan.Zero"/> quand le salarié est à
    /// l'heure (le retard n'est jamais négatif).
    /// </summary>
    public static TimeSpan? Calculer(TimeSpan? entree, TimeSpan? debutPrevu, int toleranceMinutes)
    {
        if (entree is null || debutPrevu is null)
            return null;
        var autorise = debutPrevu.Value + TimeSpan.FromMinutes(Math.Max(0, toleranceMinutes));
        return entree.Value > autorise ? entree.Value - autorise : TimeSpan.Zero;
    }
}
