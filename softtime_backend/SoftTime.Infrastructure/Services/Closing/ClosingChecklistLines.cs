using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Closing;

/// <summary>
/// Lignes de la checklist d'avancement de la paie. Chaque ligne n'utilise que des données et des règles qui existent
/// déjà ; elle ne crée aucune anomalie. Une ligne qui ne peut pas être établie objectivement est « NonDeterminable »,
/// jamais devinée.
/// </summary>
internal static class ClosingChecklistLines
{
    public const string EtatFait = "Fait";
    public const string EtatAFaire = "AFaire";
    public const string EtatInformation = "Information";
    public const string EtatNonDeterminable = "NonDeterminable";

    private const string LibelleSynchro = "Synchronisation paie";

    /// <summary>Toutes les lignes, dans l'ordre de la préparation de paie.</summary>
    internal static List<ClosingChecklistLineDto> Construire(ClosingChecklistData d)
    {
        var lignes = new List<ClosingChecklistLineDto>
        {
            LignePointages(d.Pointages, d.SalariesPointes),
            LigneAnomalies(d.Anomalies, d.Pointages, d.Corrections),
            LigneSemaines(d.SemainesAttendues, d.SemainesValidees),
            LigneExoImpo(d.ExoExactes, d.ExoRecoupantes, d.PeriodesExoDistinctes)
        };
        // Information : seulement si le calcul de la période existe.
        if (d.ExoExactes > 0)
            lignes.Add(LigneHeuresImposables(d.SalariesImposables));
        lignes.Add(LigneSynchro(d.DernierResumeSynchro));
        return lignes;
    }

    internal static ClosingChecklistLineDto LignePointages(int pointages, int salaries) =>
        pointages > 0
            ? new("Pointages", "Pointages importés", EtatFait,
                $"{pointages} pointage(s) pour {salaries} salarié(s).", pointages)
            : new("Pointages", "Pointages importés", EtatAFaire, "Aucun pointage importé sur la période.", 0);

    /// <summary>
    /// Des anomalies restantes : à faire. Aucune anomalie MAIS des pointages sans aucune heure corrigée : le traitement
    /// des pointages n'a pas été lancé, donc « zéro anomalie » ne prouve rien : à faire.
    /// </summary>
    internal static ClosingChecklistLineDto LigneAnomalies(int anomalies, int pointages, int corrections)
    {
        const string libelle = "Anomalies à arbitrer";
        if (anomalies > 0)
            return new("Anomalies", libelle, EtatAFaire, $"{anomalies} anomalie(s) restent à arbitrer.", anomalies);
        if (pointages > 0 && corrections == 0)
            return new("Anomalies", libelle, EtatAFaire,
                "Des pointages sont importés mais n'ont pas été traités (aucune heure corrigée).", 0);
        return new("Anomalies", libelle, EtatFait, "Aucune anomalie à arbitrer.", 0);
    }

    internal static ClosingChecklistLineDto LigneSemaines(int attendues, int validees)
    {
        const string libelle = "Semaines validées";
        if (attendues == 0)
            return new("Semaines", libelle, EtatInformation, "Aucune semaine planifiée à valider sur la période.", 0);
        return validees >= attendues
            ? new("Semaines", libelle, EtatFait, $"{validees} semaine(s)-salarié validée(s) sur {attendues}.", 0)
            : new("Semaines", libelle, EtatAFaire,
                $"{validees} semaine(s)-salarié validée(s) sur {attendues} : {attendues - validees} à valider.",
                attendues - validees);
    }

    internal static ClosingChecklistLineDto LigneExoImpo(int exactes, int recoupantes, int periodesDistinctes)
    {
        const string libelle = "Calcul EXO/IMPO";
        if (exactes > 0)
            return new("ExoImpo", libelle, EtatFait,
                $"{exactes} ligne(s) calculée(s) pour la période exacte.", exactes);
        if (recoupantes > 0)
            return new("ExoImpo", libelle, EtatNonDeterminable,
                $"Un calcul existe sur {periodesDistinctes} autre(s) période(s) qui recoupe(nt) celle-ci, "
                + "pas sur la période exacte.", 0);
        return new("ExoImpo", libelle, EtatAFaire, "Aucune ligne EXO/IMPO pour cette période.", 0);
    }

    internal static ClosingChecklistLineDto LigneHeuresImposables(int imposables)
    {
        var plafond = $"{OvertimeRules.ExemptCeilingHours:0.##} h exonérées";
        return new("HeuresImposables", "Heures imposables", EtatInformation,
            imposables == 0
                ? $"Aucun salarié au-delà du plafond de {plafond}."
                : $"{imposables} salarié(s) au-delà du plafond de {plafond}.",
            imposables);
    }

    /// <summary>Dernière synchronisation journalisée de la période : « Statut=SUCCES » ou « Statut=ECHEC » ; sans
    /// trace, indéterminable.</summary>
    internal static ClosingChecklistLineDto LigneSynchro(string? dernierResume)
    {
        if (string.IsNullOrWhiteSpace(dernierResume))
            return new("SynchroPaie", LibelleSynchro, EtatNonDeterminable,
                "Aucune trace de synchronisation pour cette période dans le journal "
                + "(le journal peut être désactivé).", 0);
        if (dernierResume.Contains("Statut=SUCCES", StringComparison.Ordinal))
            return new("SynchroPaie", LibelleSynchro, EtatFait,
                "Dernière synchronisation de cette période : réussie.", 0);
        if (dernierResume.Contains("Statut=ECHEC", StringComparison.Ordinal))
            return new("SynchroPaie", LibelleSynchro, EtatAFaire,
                "Dernière synchronisation de cette période : en échec.", 0);
        return new("SynchroPaie", LibelleSynchro, EtatNonDeterminable,
            "Dernière trace de synchronisation d'un statut non reconnu.", 0);
    }
}
