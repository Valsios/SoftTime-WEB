namespace SoftTime.Domain.Services;

/// <summary>
/// Taux affichés par le tableau de bord analytique.
///
/// Les formules sont isolées ici pour être documentées et testables : elles sont volontairement
/// prudentes (aucune division par zéro, arrondi à une décimale, résultat borné à 100 %) parce que
/// les dénominateurs dépendent de données SAGE / pointeuse qui peuvent être incomplètes.
/// </summary>
public static class DashboardRules
{
    /// <summary>Jours d'absence / jours planifiés (présence théorique), en pourcentage.</summary>
    public static decimal TauxAbsenteisme(int joursAbsence, int joursPlanifies) =>
        Pourcentage(joursAbsence, joursPlanifies);

    /// <summary>Jours travaillés sans retard / jours travaillés, en pourcentage.</summary>
    public static decimal TauxPonctualite(int joursTravailles, int joursRetard) =>
        Pourcentage(joursTravailles - joursRetard, joursTravailles);

    /// <summary>Taux borné : 0 % quand le dénominateur est nul, jamais négatif ni supérieur à 100 %.</summary>
    public static decimal Pourcentage(int numerateur, int denominateur)
    {
        if (denominateur <= 0)
            return 0m;
        var valeur = Math.Round(100m * numerateur / denominateur, 1, MidpointRounding.AwayFromZero);
        return Math.Clamp(valeur, 0m, 100m);
    }
}
