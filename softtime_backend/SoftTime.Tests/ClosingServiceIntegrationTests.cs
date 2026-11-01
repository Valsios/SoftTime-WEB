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
/// Test ignoré si la variable d'environnement SOFTTIME_CLOTURE_CONN n'existe pas. Sa valeur
/// (authentification Windows, jamais écrite dans le dépôt) doit viser la copie jetable
/// <c>SOFTTIME_DEMO_CLOTURE</c> ; toute autre base est refusée. Lecture seule.
/// </summary>
public sealed class ClosingIntegrationFactAttribute : FactAttribute
{
    public const string Variable = "SOFTTIME_CLOTURE_CONN";

    public ClosingIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Variable d'environnement {Variable} absente : test d'intégration de la clôture ignoré.";
    }
}

/// <summary>
/// Mesure de performance et contrôle « vérité terrain » de <see cref="ClosingService"/> sur une copie
/// de base. Variables facultatives : SOFTTIME_CLOTURE_SAGE / SOFTTIME_CLOTURE_POINTEUSE (bases
/// actives), SOFTTIME_CLOTURE_SORTIE (dossier des fichiers produits), SOFTTIME_CLOTURE_ETIQUETTE
/// (suffixe des fichiers, ex. « avant » / « apres »), SOFTTIME_CLOTURE_VERITE (fichier de vérité terrain).
/// </summary>
[Trait("Category", "Integration")]
public class ClosingServiceIntegrationTests
{
    private const string BaseAutorisee = "SOFTTIME_DEMO_CLOTURE";
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

    private static IEnumerable<(string Suffixe, ClosingRulesOptions Options)> ModesOptions()
    {
        yield return ("defaut", ClosingRulesOptions.Default);
        yield return ("sans-semaines-validees", new ClosingRulesOptions { IgnorerSemainesValidees = false });
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
        if (!string.Equals(catalogue, BaseAutorisee, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Base refusée : seule la copie {BaseAutorisee} est autorisée.");

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
