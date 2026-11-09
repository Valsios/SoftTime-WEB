namespace SoftTime.Infrastructure.Services.Punches;

/// <summary>Dates et heures d'une anomalie arbitrée : entrée, pauses et sortie.</summary>
internal readonly record struct AnomalyHours(
    DateTime? DateIn, TimeSpan? HEntree, TimeSpan? HPs, TimeSpan? HPe, DateTime? DateOut, TimeSpan? HSortie)
{
    /// <summary>
    /// La sortie suit l'entrée et, sur une même journée, les heures de pause restent entre les deux.
    /// Contrôle seulement les valeurs complètes (entrée et sortie datées).
    /// </summary>
    public void Valider()
    {
        if (!DateIn.HasValue || !DateOut.HasValue || !HEntree.HasValue || !HSortie.HasValue)
            return;

        var entree = DateIn.Value.Date.Add(HEntree.Value);
        var sortie = DateOut.Value.Date.Add(HSortie.Value);
        if (sortie <= entree)
            throw new DomainException("La sortie doit être postérieure à l'entrée.");

        if (DateIn.Value.Date != DateOut.Value.Date)
            return;
        foreach (var pause in new[] { HPs, HPe }.Where(h => h.HasValue).Select(h => h!.Value))
        {
            var t = DateIn.Value.Date.Add(pause);
            if (t < entree || t > sortie)
                throw new DomainException(
                    "Les heures de pause doivent être comprises entre l'entrée et la sortie.");
        }
    }
}
