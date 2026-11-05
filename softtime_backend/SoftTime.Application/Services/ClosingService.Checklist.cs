using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Checklist d'avancement de la paie d'une période (lecture seule). Chaque ligne n'utilise que des données et des
/// règles qui existent déjà ; elle ne crée aucune anomalie et n'entre ni dans le score ni dans le statut de l'analyse.
/// Une ligne qui ne peut pas être établie objectivement est « NonDeterminable », jamais devinée.
/// </summary>
public partial class ClosingService
{
    public const string EtatFait = "Fait";
    public const string EtatAFaire = "AFaire";
    public const string EtatInformation = "Information";
    public const string EtatNonDeterminable = "NonDeterminable";

    /// <summary>Durée de réutilisation de la checklist (60 s, ou moins si le délai glissant est plus court ; 0 : aucun cache).</summary>
    private const int DureeCacheChecklistSecondes = 60;

    /// <summary>Un cache par instance de réglages (donc par processus en production, isolé dans chaque test).</summary>
    private static readonly ConditionalWeakTable<ClosingOptions, ChecklistCache> CachesChecklist = new();

    /// <summary>Cache mémoire de la checklist : la clé porte base, période, département et périmètre de l'utilisateur.</summary>
    private sealed class ChecklistCache
    {
        private const int MaxEntrees = 16;
        private readonly ConcurrentDictionary<string, (DateTimeOffset Expire, ClosingChecklistDto Valeur)> _entrees = new();

        public bool TryGet(string cle, DateTimeOffset maintenant, out ClosingChecklistDto valeur)
        {
            if (_entrees.TryGetValue(cle, out var e) && e.Expire > maintenant)
            {
                valeur = e.Valeur;
                return true;
            }
            valeur = null!;
            return false;
        }

        public void Set(string cle, ClosingChecklistDto valeur, DateTimeOffset maintenant, TimeSpan duree)
        {
            foreach (var (k, e) in _entrees)
                if (e.Expire <= maintenant)
                    _entrees.TryRemove(k, out _);
            while (_entrees.Count >= MaxEntrees && _entrees.Keys.FirstOrDefault() is string ancienne)
                _entrees.TryRemove(ancienne, out _);
            _entrees[cle] = (maintenant + duree, valeur);
        }
    }

    /// <summary>
    /// Checklist de la période (mois civil précédent sans dates), pour le département demandé.
    /// <paramref name="perimetre"/> identifie le périmètre de droits de l'appelant et entre dans la clé du cache :
    /// deux utilisateurs ne partagent jamais une entrée. <see cref="ClosingChecklistRequest.ReuseCached"/> (faux par
    /// défaut) autorise la réutilisation de la dernière checklist pendant 60 s au plus.
    /// </summary>
    public async Task<ClosingChecklistDto> ChecklistAsync(ClosingChecklistRequest request, string perimetre, CancellationToken ct = default)
    {
        var periode = AvecPeriodeParDefaut(new ClosingAnalysisRequest(request.From, request.To, request.Departement), Today());
        Valider(periode, _reglages.MaxPeriodDays);
        await _tenant.EnsureAuthorizedAsync(ct);

        var from = periode.From.Date;
        var to = periode.To.Date;
        var cache = CachesChecklist.GetOrCreateValue(_reglages);
        var cle = string.Join('|', _tenant.SageDb.Trim().ToUpperInvariant(), _tenant.PointeuseDb.Trim().ToUpperInvariant(),
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (request.Departement ?? string.Empty).Trim().ToUpperInvariant(), perimetre);
        var maintenant = _horloge.GetUtcNow();
        if (_reglages.CacheSeconds > 0 && request.ReuseCached && cache.TryGet(cle, maintenant, out var conservee))
            return conservee;

        var resultat = await ConstruireChecklistAsync(from, to, request.Departement, ct);
        if (_reglages.CacheSeconds > 0)
            cache.Set(cle, resultat, maintenant, TimeSpan.FromSeconds(Math.Min(DureeCacheChecklistSecondes, _reglages.CacheSeconds)));
        return resultat;
    }

    private async Task<ClosingChecklistDto> ConstruireChecklistAsync(DateTime from, DateTime to, string? departement, CancellationToken ct)
    {
        var sageDb = _tenant.SageDb;
        var pointeuseDb = _tenant.PointeuseDb;
        var finExclusive = to.AddDays(1);

        // Périmètre : mêmes cartes que l'analyse (base, pointeuse, matricule renseigné, département).
        var cartes = (await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == sageDb, ct))
            .Where(c => CorrespondPointeuse(c.POINTEUSE_BDD, pointeuseDb))
            .Where(c => !string.IsNullOrWhiteSpace(c.SAGE_MATRICULE))
            .Where(c => string.IsNullOrWhiteSpace(departement)
                        || string.Equals(c.DEPARTEMENT?.Trim(), departement.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var cles = cartes.Select(c => MatriculeIdentity.Cle(c.SAGE_MATRICULE)).ToHashSet(StringComparer.Ordinal);
        var cleParIdSal = new Dictionary<int, string>();
        foreach (var carte in cartes)
            cleParIdSal[carte.ID] = MatriculeIdentity.Cle(carte.SAGE_MATRICULE);

        // a) Pointages importés.
        var repoPointage = _uow.Repository<T_POINTAGE>();
        var pointages = (await repoPointage.ToListAsync(repoPointage.Query()
                .Where(p => p.NOM_BDD_SAGE == sageDb && p.DATE_POINTAGE >= from && p.DATE_POINTAGE < finExclusive)
                .Select(p => new T_POINTAGE { IDPOINTAGE = p.IDPOINTAGE, MATRICULE_SAGE = p.MATRICULE_SAGE, NOM_BDD_POINTEUSE = p.NOM_BDD_POINTEUSE }), ct))
            .Where(p => CorrespondPointeuse(p.NOM_BDD_POINTEUSE, pointeuseDb))
            .Where(p => cles.Contains(MatriculeIdentity.Cle(p.MATRICULE_SAGE)))
            .ToList();
        var salariesPointes = pointages.Select(p => MatriculeIdentity.Cle(p.MATRICULE_SAGE)).Distinct(StringComparer.Ordinal).Count();

        // b) Anomalies restant à arbitrer, et traitement des pointages (corrections générées).
        var anomalies = (await _uow.Repository<T_HEUREANOMALIE>().ListAsync(
                a => a.BDD_SAGE == sageDb && a.DATE_POINTAGE_IN >= from && a.DATE_POINTAGE_IN < finExclusive, ct))
            .Count(a => cles.Contains(MatriculeIdentity.Cle(a.MATRICULE_SAGE)));
        var corrections = (await _uow.Repository<T_HEURECORRIGER>().ListAsync(
                h => h.BDD_SAGE == sageDb && h.DATE_POINTAGE_IN >= from && h.DATE_POINTAGE_IN < finExclusive, ct))
            .Count(h => cles.Contains(MatriculeIdentity.Cle(h.MATRICULE_SAGE)));

        // c) Semaines à valider : une semaine appartient à la période qui contient son dimanche (comme le calcul EXO/IMPO).
        var lundiMin = from.AddDays(-6);
        var lundiMax = to.AddDays(-6);
        var repoPlanning = _uow.Repository<T_PLANNING_SAL>();
        var plannings = (await repoPlanning.ToListAsync(repoPlanning.Query()
                .Where(p => p.BDD_SAGE == sageDb && p.DateP >= lundiMin && p.DateP <= to)
                .Select(p => new T_PLANNING_SAL { ID = p.ID, IDSAL = p.IDSAL, DateP = p.DateP, HA = p.HA, HD = p.HD, Pause = p.Pause, OffP = p.OffP }), ct))
            .Where(p => p.DateP.HasValue && cleParIdSal.ContainsKey(p.IDSAL))
            .ToList();
        var attendues = plannings
            .GroupBy(p => (Cle: cleParIdSal[p.IDSAL], Jour: p.DateP!.Value.Date))
            .Select(g => (g.Key, Retenu: g.OrderBy(p => AbsenceReportRules.EstJourOff(p) ? 1 : 0).ThenBy(p => p.ID).First()))
            .Where(x => !AbsenceReportRules.EstJourOff(x.Retenu) && AbsenceReportRules.CalculerDureeHeures(x.Retenu) is not null)
            .Select(x => (x.Key.Cle, Lundi: LundiDe(x.Key.Jour)))
            .Where(x => x.Lundi >= lundiMin && x.Lundi <= lundiMax)
            .ToHashSet();
        var validees = (await _uow.Repository<T_HS_VALIDER>().ListAsync(
                v => v.BDD_SAGE == sageDb && v.VALIDATION_HS == true && v.DATE_DEB >= lundiMin && v.DATE_DEB <= lundiMax, ct))
            .Where(v => v.DATE_DEB.HasValue && !string.IsNullOrWhiteSpace(v.MATRICULES))
            .Select(v => (Cle: MatriculeIdentity.Cle(v.MATRICULES), Lundi: v.DATE_DEB!.Value.Date))
            .ToHashSet();
        var semainesAttendues = attendues.Count;
        var semainesValidees = attendues.Count(validees.Contains);

        // d) et e) Calcul EXO/IMPO : lignes de la période exacte, ou d'une période qui la recoupe.
        var exoRecoupant = (await _uow.Repository<T_HSExoImp>().ListAsync(
                h => h.BDD_SAGE == sageDb && h.PeriodeDebut <= to && h.PeriodeFin >= from, ct))
            .Where(h => cles.Contains(MatriculeIdentity.Cle(h.Matricule)))
            .ToList();
        var exoExact = exoRecoupant.Where(h => h.PeriodeDebut?.Date == from && h.PeriodeFin?.Date == to).ToList();

        // f) Synchronisation paie : seule trace stockée = ligne « SYNC_PAIE » du journal d'audit, pour la même période.
        var libellePeriode = $"Période={from:yyyy-MM-dd}..{to:yyyy-MM-dd}";
        var synchros = (await _uow.Repository<suivi_JOURNAL>().ListAsync(
                j => j.ACTIONS == "SYNC_PAIE" && j.BASE_SAGE == sageDb && j.NOUVEAUX != null && j.NOUVEAUX.Contains(libellePeriode), ct))
            .OrderByDescending(j => j.DATE_UTC)
            .ToList();

        var lignes = new List<ClosingChecklistLineDto>
        {
            LignePointages(pointages.Count, salariesPointes),
            LigneAnomalies(anomalies, pointages.Count, corrections),
            LigneSemaines(semainesAttendues, semainesValidees),
            LigneExoImpo(exoExact.Count, exoRecoupant.Count, exoRecoupant.Select(h => (h.PeriodeDebut, h.PeriodeFin)).Distinct().Count())
        };
        // e) Information : seulement si le calcul de la période existe.
        if (exoExact.Count > 0)
        {
            var imposables = exoExact.Count(h => (h.I130 ?? 0m) + (h.I150 ?? 0m) > 0m);
            lignes.Add(new ClosingChecklistLineDto("HeuresImposables", "Heures imposables", EtatInformation,
                imposables == 0
                    ? $"Aucun salarié au-delà du plafond de {OvertimeRules.ExemptCeilingHours:0.##} h exonérées."
                    : $"{imposables} salarié(s) au-delà du plafond de {OvertimeRules.ExemptCeilingHours:0.##} h exonérées.",
                imposables));
        }
        lignes.Add(LigneSynchro(synchros.Select(j => j.NOUVEAUX ?? string.Empty).FirstOrDefault()));
        return new ClosingChecklistDto(from, to, string.IsNullOrWhiteSpace(departement) ? null : departement.Trim(), lignes);
    }

    private static DateTime LundiDe(DateTime jour) => jour.Date.AddDays(-(((int)jour.DayOfWeek + 6) % 7));

    internal static ClosingChecklistLineDto LignePointages(int pointages, int salaries) =>
        pointages > 0
            ? new("Pointages", "Pointages importés", EtatFait, $"{pointages} pointage(s) pour {salaries} salarié(s).", pointages)
            : new("Pointages", "Pointages importés", EtatAFaire, "Aucun pointage importé sur la période.", 0);

    /// <summary>
    /// Des anomalies restantes : à faire. Aucune anomalie MAIS des pointages sans aucune heure corrigée : le traitement
    /// des pointages n'a pas été lancé, donc « zéro anomalie » ne prouve rien : à faire.
    /// </summary>
    internal static ClosingChecklistLineDto LigneAnomalies(int anomalies, int pointages, int corrections)
    {
        if (anomalies > 0)
            return new("Anomalies", "Anomalies à arbitrer", EtatAFaire, $"{anomalies} anomalie(s) restent à arbitrer.", anomalies);
        if (pointages > 0 && corrections == 0)
            return new("Anomalies", "Anomalies à arbitrer", EtatAFaire, "Des pointages sont importés mais n'ont pas été traités (aucune heure corrigée).", 0);
        return new("Anomalies", "Anomalies à arbitrer", EtatFait, "Aucune anomalie à arbitrer.", 0);
    }

    internal static ClosingChecklistLineDto LigneSemaines(int attendues, int validees)
    {
        if (attendues == 0)
            return new("Semaines", "Semaines validées", EtatInformation, "Aucune semaine planifiée à valider sur la période.", 0);
        return validees >= attendues
            ? new("Semaines", "Semaines validées", EtatFait, $"{validees} semaine(s)-salarié validée(s) sur {attendues}.", 0)
            : new("Semaines", "Semaines validées", EtatAFaire, $"{validees} semaine(s)-salarié validée(s) sur {attendues} : {attendues - validees} à valider.", attendues - validees);
    }

    internal static ClosingChecklistLineDto LigneExoImpo(int exactes, int recoupantes, int periodesDistinctes)
    {
        if (exactes > 0)
            return new("ExoImpo", "Calcul EXO/IMPO", EtatFait, $"{exactes} ligne(s) calculée(s) pour la période exacte.", exactes);
        if (recoupantes > 0)
            return new("ExoImpo", "Calcul EXO/IMPO", EtatNonDeterminable,
                $"Un calcul existe sur {periodesDistinctes} autre(s) période(s) qui recoupe(nt) celle-ci, pas sur la période exacte.", 0);
        return new("ExoImpo", "Calcul EXO/IMPO", EtatAFaire, "Aucune ligne EXO/IMPO pour cette période.", 0);
    }

    /// <summary>Dernière synchronisation journalisée de la période : « Statut=SUCCES » ou « Statut=ECHEC » ; sans trace, indéterminable.</summary>
    internal static ClosingChecklistLineDto LigneSynchro(string? dernierResume)
    {
        const string libelle = "Synchronisation paie";
        if (string.IsNullOrWhiteSpace(dernierResume))
            return new("SynchroPaie", libelle, EtatNonDeterminable,
                "Aucune trace de synchronisation pour cette période dans le journal (le journal peut être désactivé).", 0);
        if (dernierResume.Contains("Statut=SUCCES", StringComparison.Ordinal))
            return new("SynchroPaie", libelle, EtatFait, "Dernière synchronisation de cette période : réussie.", 0);
        if (dernierResume.Contains("Statut=ECHEC", StringComparison.Ordinal))
            return new("SynchroPaie", libelle, EtatAFaire, "Dernière synchronisation de cette période : en échec.", 0);
        return new("SynchroPaie", libelle, EtatNonDeterminable, "Dernière trace de synchronisation d'un statut non reconnu.", 0);
    }
}
