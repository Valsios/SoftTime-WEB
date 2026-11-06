using System.Diagnostics;
using System.Globalization;
using System.Text;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SoftTime.Application;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Models;
using SoftTime.Domain.Services;
using SoftTime.Infrastructure;

using Xunit.Abstractions;

namespace SoftTime.Tests;

/// <summary>
/// Mesure de performance et contrôle « vérité terrain » de <see cref="ClosingService"/> sur une copie
/// de base. Variables facultatives : SOFTTIME_CLOTURE_SAGE / SOFTTIME_CLOTURE_POINTEUSE (bases
/// actives), SOFTTIME_CLOTURE_SORTIE (dossier des fichiers produits), SOFTTIME_CLOTURE_ETIQUETTE
/// (suffixe des fichiers, ex. « avant » / « apres »), SOFTTIME_CLOTURE_VERITE (fichier de vérité terrain).
/// </summary>
[Trait("Category", "Integration")]
public class ClosingServiceIntegrationTests
{
    // Copies jetables : la base de démonstration (anomalies injectées) et la copie de référence intacte.
    private static readonly string[] BasesAutorisees = { "SOFTTIME_DEMO_CLOTURE", "SOFTTIME_DEMO_CLOTURE_REF" };
    private static readonly DateTime Novembre = new(2025, 11, 1);
    private static readonly DateTime FinNovembre = new(2025, 11, 30);

    private readonly ITestOutputHelper _sortie;

    public ClosingServiceIntegrationTests(ITestOutputHelper sortie) => _sortie = sortie;

    [ClosingIntegrationFact]
    public async Task Performance_ThreeMonths_LoadAndComputeMeasuredSeparately()
    {
        using var scope = Fournisseur().CreateScope();
        var service = ActivatorUtilities.CreateInstance<ClosingService>(scope.ServiceProvider);
        var request = new ClosingAnalysisRequest(new DateTime(2025, 9, 1), FinNovembre);

        var rapport = new StringBuilder();
        rapport.AppendLine("passage;chargement_ms;calcul_ms;cartes;plannings;pointages;corrections;semaines_validees;jours_controles;anomalies;score");
        ClosingData? donnees = null;
        for (var passage = 1; passage <= 2; passage++)
        {
            var chrono = Stopwatch.StartNew();
            donnees = await service.ChargerAsync(request, CancellationToken.None);
            var chargement = chrono.ElapsedMilliseconds;
            chrono.Restart();
            var resultat = ClosingService.Calculer(donnees, request);
            var calcul = chrono.ElapsedMilliseconds;
            rapport.AppendLine(string.Join(";", passage, chargement, calcul, donnees.Cartes.Count, donnees.Plannings.Count,
                donnees.Pointages.Count, donnees.Corrections.Count, donnees.SemainesValidees.Count,
                resultat.JoursEmployeControles, resultat.NombreAnomalies, resultat.Score?.ToString() ?? "null"));
        }

        // Détail du calcul : chaque règle, sur la période demandée seule.
        var d = donnees!;
        var debut = request.From;
        var fin = request.To;
        rapport.AppendLine("regle;ms");
        void Mesurer(string regle, Action action)
        {
            var chrono = Stopwatch.StartNew();
            action();
            rapport.AppendLine($"{regle};{chrono.ElapsedMilliseconds}");
        }
        Mesurer("PointagesIncomplets", () => PayrollClosingRules.PointagesIncomplets(d.Cartes, d.Plannings, d.Pointages, d.Corrections, d.SemainesValidees, debut, fin, d.MultiPointage));
        Mesurer("PointagesManquants", () => PayrollClosingRules.PointagesManquants(d.Cartes, d.Plannings, d.Pointages, d.Corrections, d.Conges, d.Feries, debut, fin, d.DateDuJour));
        Mesurer("PlanningsSansPointage", () => PayrollClosingRules.PlanningsSansPointage(d.Cartes, d.Plannings, d.Pointages, d.Corrections, d.Conges, d.Feries, debut, fin, d.DateDuJour));
        Mesurer("PointagesSansPlanning", () => PayrollClosingRules.PointagesSansPlanning(d.Cartes, d.Plannings, d.Pointages, debut, fin));
        Mesurer("DoublonsExacts", () => PayrollClosingRules.DoublonsExacts(d.Pointages, debut, fin));
        Mesurer("QuasiDoublons", () => PayrollClosingRules.QuasiDoublons(d.Pointages, debut, fin));
        Mesurer("FeriesNonAppliques", () => PayrollClosingRules.FeriesNonAppliques(d.Cartes, d.Plannings, d.Corrections, d.Feries, debut, fin, d.DateDuJour));
        Mesurer("AbsenceReportRules.Calculer", () => AbsenceReportRules.Calculer(d.Plannings, d.Cartes, d.Corrections, d.Conges, d.Feries, debut, fin, d.DateDuJour));
        Mesurer("chargement_conges_SAGE", () => scope.ServiceProvider.GetRequiredService<SageLeaveCalendar>().LoadAsync().GetAwaiter().GetResult());

        var process = Process.GetCurrentProcess();
        process.Refresh();
        rapport.AppendLine($"pic_memoire_processus_mo;{process.PeakWorkingSet64 / (1024 * 1024)}");
        rapport.AppendLine($"memoire_geree_mo;{GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024)}");
        Ecrire("performance", rapport.ToString());

        // Anomalies de novembre, pour la comparaison avec la vérité terrain.
        foreach (var (suffixe, options) in ModesOptions())
        {
            var anomalies = ClosingService.Controler(donnees!, Novembre, FinNovembre, options).Anomalies;
            var csv = new StringBuilder("Type;Matricule;Date;Detail\n");
            foreach (var a in anomalies)
                csv.Append(a.Type).Append(';').Append(a.Matricule).Append(';')
                    .Append(a.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(';')
                    .Append(a.Detail).Append('\n');
            Ecrire($"anomalies-{suffixe}", csv.ToString(), "csv");
        }
    }

    [ClosingIntegrationFact]
    public async Task GroundTruth_AllInjectedAnomaliesAreDetected()
    {
        var verite = Environment.GetEnvironmentVariable("SOFTTIME_CLOTURE_VERITE");
        if (string.IsNullOrWhiteSpace(verite) || !File.Exists(verite))
        {
            _sortie.WriteLine("SOFTTIME_CLOTURE_VERITE absente : comparaison à la vérité terrain non faite.");
            return;
        }

        // Format : Type;Matricule(clé canonique);Date(yyyy-MM-dd);AttenduParDefaut(1/0)
        var attendus = File.ReadAllLines(verite).Skip(1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(';'))
            .Select(c => (Cle: (c[0], c[1], c[2]), ParDefaut: c[3].Trim() == "1"))
            .ToList();

        using var scope = Fournisseur().CreateScope();
        var service = ActivatorUtilities.CreateInstance<ClosingService>(scope.ServiceProvider);
        var request = new ClosingAnalysisRequest(Novembre, FinNovembre);
        var donnees = await service.ChargerAsync(request, CancellationToken.None);

        var rapport = new StringBuilder("mode;type;attendues;detectees;manquees;fausses_alertes;preexistantes\n");
        var manquees = new List<string>();
        foreach (var (suffixe, options) in ModesOptions())
        {
            var detectees = ClosingService.Controler(donnees, Novembre, FinNovembre, options).Anomalies
                .Select(a => (a.Type.ToString(), a.Matricule, a.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                .ToHashSet();
            var avant = LireAnomalies(Path.Combine(Dossier() ?? string.Empty, $"anomalies-{suffixe}-avant.csv"));
            var attendusMode = attendus
                .Where(a => a.ParDefaut || suffixe == "sans-semaines-validees")
                .Select(a => a.Cle)
                .ToHashSet();
            var tousInjectes = attendus.Select(a => a.Cle).ToHashSet();

            foreach (var type in Enum.GetValues<ClosingAnomalyType>().Select(t => t.ToString()))
            {
                var att = attendusMode.Where(a => a.Item1 == type).ToList();
                var det = att.Count(detectees.Contains);
                var fausses = detectees.Count(d => d.Item1 == type && !tousInjectes.Contains(d) && !avant.Contains(d));
                var pre = detectees.Count(d => d.Item1 == type && !tousInjectes.Contains(d) && avant.Contains(d));
                rapport.AppendLine(string.Join(";", suffixe, type, att.Count, det, att.Count - det, fausses, pre));
                manquees.AddRange(att.Where(a => !detectees.Contains(a)).Select(a => $"{suffixe}:{a.Item1}:{a.Item2}:{a.Item3}"));
            }

            var csv = new StringBuilder("Type;Matricule;Date;Statut\n");
            foreach (var d in detectees.OrderBy(d => d.Item3).ThenBy(d => d.Item2).ThenBy(d => d.Item1))
            {
                var statut = tousInjectes.Contains(d) ? "injectee" : avant.Contains(d) ? "preexistante" : "fausse_alerte";
                csv.Append(d.Item1).Append(';').Append(d.Item2).Append(';').Append(d.Item3).Append(';').Append(statut).Append('\n');
            }
            Ecrire($"detection-{suffixe}", csv.ToString(), "csv");
        }

        Ecrire("verite-terrain-resultat", rapport.ToString(), "csv", sansEtiquette: true);
        Assert.True(manquees.Count == 0, "Anomalies injectées non détectées : " + string.Join(", ", manquees));
    }

    /// <summary>
    /// Empreinte de non-régression d'une période (SOFTTIME_CLOTURE_DEBUT / SOFTTIME_CLOTURE_FIN, novembre 2025
    /// par défaut) : score, comptages, départements, comparaison et hash SHA-256 de la liste complète.
    /// </summary>
    [ClosingIntegrationFact]
    public async Task Fingerprint_PeriodResult()
    {
        var debut = DateLue("SOFTTIME_CLOTURE_DEBUT") ?? Novembre;
        var fin = DateLue("SOFTTIME_CLOTURE_FIN") ?? FinNovembre;
        using var scope = Fournisseur().CreateScope();
        var service = ActivatorUtilities.CreateInstance<ClosingService>(scope.ServiceProvider);
        var reglages = Reglages();
        var request = new ClosingAnalysisRequest(debut, fin, Options: reglages, IncludeComparison: Comparaison(defaut: true));
        var donnees = await service.ChargerAsync(request, CancellationToken.None);
        var resultat = ClosingService.Calculer(donnees, request);
        var anomalies = ClosingService.Controler(donnees, debut, fin, reglages).Anomalies;

        static string Comptes(IEnumerable<ClosingCountDto> c) => string.Join(",", c.Select(x => $"{x.Cle}={x.Nombre}"));
        var e = new StringBuilder();
        e.AppendLine($"periode;{debut:yyyy-MM-dd};{fin:yyyy-MM-dd}");
        e.AppendLine($"score;{resultat.Score?.ToString() ?? "null"}");
        e.AppendLine($"jours_controles;{resultat.JoursEmployeControles}");
        e.AppendLine($"anomalies;{resultat.NombreAnomalies}");
        e.AppendLine($"par_gravite;{Comptes(resultat.ParGravite.Where(g => g.Cle != nameof(ClosingSeverity.Information) || g.Nombre > 0))}");
        e.AppendLine($"par_type;{Comptes(resultat.ParType)}");
        foreach (var dep in resultat.ParDepartement)
            e.AppendLine($"departement;{dep.Departement ?? "(aucun)"};{dep.Score?.ToString() ?? "null"};{dep.JoursEmployeControles};{dep.NombreAnomalies};{Comptes(dep.ParType)}");
        var c = resultat.Comparaison;
        e.AppendLine(c is null
            ? "comparaison;null"
            : $"comparaison;{c.From:yyyy-MM-dd};{c.To:yyyy-MM-dd};{c.ScorePrecedent?.ToString() ?? "null"};{c.JoursEmployeControlesPrecedent};{c.EcartScore?.ToString() ?? "null"};"
              + string.Join(",", c.ParType.Select(t => $"{t.Type}={t.Precedent}/{t.Actuel}/{t.Ecart}")));
        e.AppendLine($"hash_anomalies;{Hash(anomalies.Select(a => string.Join(";", a.Type, a.Severity, a.Matricule,
            a.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), a.Detail, a.Heures?.ToString(CultureInfo.InvariantCulture) ?? "")))}");
        e.AppendLine($"hash_page1;{Hash(resultat.Anomalies.Items.Select(a => string.Join(";", a.Type, a.Gravite, a.Poids, a.Matricule,
            a.Departement, a.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), a.Detail, a.Heures?.ToString(CultureInfo.InvariantCulture) ?? "")))}");
        // Preuves de non-régression indépendantes du libellé des détails (reformulé en phase 3c pour
        // DoublonExact, QuasiDoublon et FerieNonApplique) : tout le reste de la ligne, puis les lignes
        // complètes des autres types.
        static string Jour(ClosingAnomaly a) => a.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        e.AppendLine($"hash_sans_detail;{Hash(anomalies.Select(a => string.Join(";", a.Type, a.Severity, a.Matricule, Jour(a), a.Heures?.ToString(CultureInfo.InvariantCulture) ?? "")))}");
        var typesReformules = new[] { ClosingAnomalyType.DoublonExact, ClosingAnomalyType.QuasiDoublon, ClosingAnomalyType.FerieNonApplique };
        e.AppendLine($"hash_details_inchanges;{Hash(anomalies.Where(a => !typesReformules.Contains(a.Type)).Select(a => string.Join(";", a.Type, a.Severity, a.Matricule, Jour(a), a.Detail, a.Heures?.ToString(CultureInfo.InvariantCulture) ?? "")))}");
        Ecrire("empreinte", e.ToString());
        // Hors empreinte (son format reste celui d'avant la phase 3c) : statut et salariés non contrôlés.
        Ecrire("synthese", string.Join("\n",
            $"statut;{resultat.Statut ?? "null"}",
            $"seuil;{reglages.CriticalRatioBlocking}",
            $"salaries_sans_planning_exclus;{resultat.SalariesSansPlanningExclus}",
            $"critiques_sur_jours;{Comptes(resultat.ParGravite.Where(g => g.Cle == nameof(ClosingSeverity.Critique)))};{resultat.JoursEmployeControles}") + "\n");
    }

    /// <summary>
    /// Mesure d'un scénario (une période, à lancer seul dans son processus pour un pic mémoire
    /// significatif) : chargement et calcul à froid puis à chaud via <see cref="ClosingService.AnalyserAsync"/>.
    /// </summary>
    [ClosingIntegrationFact]
    public async Task Performance_SingleScenario()
    {
        var debut = DateLue("SOFTTIME_CLOTURE_DEBUT") ?? Novembre;
        var fin = DateLue("SOFTTIME_CLOTURE_FIN") ?? FinNovembre;
        var request = new ClosingAnalysisRequest(debut, fin, IncludeComparison: Comparaison(defaut: false));
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var memoireDepart = process.WorkingSet64;

        var mesures = new List<string>();
        for (var passage = 1; passage <= 2; passage++)
        {
            using var scope = Fournisseur().CreateScope();
            var service = ActivatorUtilities.CreateInstance<ClosingService>(scope.ServiceProvider);
            var chrono = Stopwatch.StartNew();
            var donnees = await service.ChargerAsync(request, CancellationToken.None);
            var chargement = chrono.ElapsedMilliseconds;
            var heapCharge = GC.GetTotalMemory(forceFullCollection: true);
            chrono.Restart();
            var resultat = ClosingService.Calculer(donnees, request);
            var calcul = chrono.ElapsedMilliseconds;
            mesures.Add($"{(passage == 1 ? "froid" : "chaud")};{chargement};{calcul};{chargement + calcul};{heapCharge / (1024 * 1024)};{resultat.NombreAnomalies};{resultat.Score?.ToString() ?? "null"};{(resultat.Comparaison is null ? "non" : "oui")}");
        }

        process.Refresh();
        Ecrire("perf", $"periode;{debut:yyyy-MM-dd};{fin:yyyy-MM-dd}\n"
            + "passage;chargement_ms;calcul_ms;total_ms;tas_gere_apres_chargement_mo;anomalies;score;comparaison\n"
            + string.Join("\n", mesures) + "\n"
            + $"memoire_processus_depart_mo;{memoireDepart / (1024 * 1024)}\n"
            + $"pic_memoire_processus_mo;{process.PeakWorkingSet64 / (1024 * 1024)}\n");
    }

    /// <summary>
    /// Réglages du scénario : ceux du code par défaut, ou, avec SOFTTIME_CLOTURE_REGLAGES=anciens, les
    /// réglages d'avant la phase 3c (tolérance 120 s, doublons Mineure, férié Importante, sans exclusion
    /// des salariés sans planning, seuil 0,02) pour prouver la non-régression.
    /// </summary>
    private static ClosingRulesOptions Reglages() =>
        Environment.GetEnvironmentVariable("SOFTTIME_CLOTURE_REGLAGES") == "anciens"
            ? new ClosingRulesOptions
            {
                DuplicateToleranceSeconds = 120,
                ExclureSansPlanningSurPeriode = false,
                CriticalRatioBlocking = 0.02m,
                Gravites = new Dictionary<ClosingAnomalyType, ClosingSeverity>
                {
                    [ClosingAnomalyType.DoublonExact] = ClosingSeverity.Mineure,
                    [ClosingAnomalyType.QuasiDoublon] = ClosingSeverity.Mineure,
                    [ClosingAnomalyType.FerieNonApplique] = ClosingSeverity.Importante
                }
            }
            : ClosingRulesOptions.Default;

    private static string Hash(IEnumerable<string> lignes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var octets = Encoding.UTF8.GetBytes(string.Join("\n", lignes));
        return Convert.ToHexString(sha.ComputeHash(octets));
    }

    /// <summary>SOFTTIME_CLOTURE_COMPARAISON = 1 / 0 ; absente : valeur par défaut du scénario.</summary>
    private static bool Comparaison(bool defaut) =>
        Environment.GetEnvironmentVariable("SOFTTIME_CLOTURE_COMPARAISON") switch
        {
            "1" => true,
            "0" => false,
            _ => defaut
        };

    private static DateTime? DateLue(string variable) =>
        DateTime.TryParseExact(Environment.GetEnvironmentVariable(variable), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    private static IEnumerable<(string Suffixe, ClosingRulesOptions Options)> ModesOptions()
    {
        yield return ("defaut", Reglages());
        yield return ("sans-semaines-validees", Reglages() with { IgnorerSemainesValidees = false });
    }

    private static HashSet<(string, string, string)> LireAnomalies(string chemin) =>
        File.Exists(chemin)
            ? File.ReadAllLines(chemin).Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(';'))
                .Select(c => (c[0], c[1], c[2]))
                .ToHashSet()
            : new HashSet<(string, string, string)>();

    private static ServiceProvider Fournisseur()
    {
        var connexion = Environment.GetEnvironmentVariable(ClosingIntegrationFactAttribute.Variable)!;
        var catalogue = new SqlConnectionStringBuilder(connexion).InitialCatalog;
        if (!BasesAutorisees.Contains(catalogue, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Base refusée : seules les copies {string.Join(", ", BasesAutorisees)} sont autorisées.");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SoftTime"] = connexion })
            .Build();
        var company = new FakeCompanyContext();
        company.Set(
            Environment.GetEnvironmentVariable("SOFTTIME_CLOTURE_SAGE") ?? "SFOI_PAIE2025",
            Environment.GetEnvironmentVariable("SOFTTIME_CLOTURE_POINTEUSE") ?? "sfoiptg");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<ICompanyContext>(_ => company);
        services.AddScoped<ICurrentUser, FakeCurrentUser>();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static string? Dossier() => Environment.GetEnvironmentVariable("SOFTTIME_CLOTURE_SORTIE");

    private void Ecrire(string nom, string contenu, string extension = "txt", bool sansEtiquette = false)
    {
        _sortie.WriteLine($"--- {nom} ---");
        _sortie.WriteLine(contenu.Length > 4000 ? contenu[..4000] + "…" : contenu);
        var dossier = Dossier();
        if (string.IsNullOrWhiteSpace(dossier))
            return;
        var etiquette = sansEtiquette ? string.Empty : "-" + (Environment.GetEnvironmentVariable("SOFTTIME_CLOTURE_ETIQUETTE") ?? "run");
        File.WriteAllText(Path.Combine(dossier, $"{nom}{etiquette}.{extension}"), contenu, new UTF8Encoding(false));
    }
}
