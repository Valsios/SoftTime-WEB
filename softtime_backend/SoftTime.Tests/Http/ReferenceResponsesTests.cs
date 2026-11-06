using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Tests.Http;

/// <summary>
/// Réponses de référence (non-régression). Si la variable d'environnement SOFTTIME_REF_OUT désigne un dossier, enregistre pour trois profils
/// (administrateur, porte 1 seule, porte 2 seule) la réponse de CHAQUE route GET sans paramètre de chemin, sur des données fictives fixes :
/// statut, empreinte SHA-256 et corps. Le même test, lancé sur la tête de dev-refonte puis sur la branche candidate, produit deux dossiers
/// que le script de comparaison confronte octet pour octet. Sans la variable, le test ne fait que vérifier que le balayage tourne.
/// </summary>
public class ReferenceResponsesTests : IClassFixture<ApiFactory>
{
    private const decimal Porte1Id = 10501;
    private const decimal Porte2Id = 10502;

    private readonly ApiFactory _f;
    private readonly HttpClient _http;

    public ReferenceResponsesTests(ApiFactory factory)
    {
        _f = factory;
        _http = factory.CreateClient();
        lock (factory.Store)
            Seed(factory.Store);
    }

    private static void Seed(SnapshotInMemoryUnitOfWork store)
    {
        var users = store.GetRepo<T_RESPONSABLE>().Items;
        if (users.Any(u => u.IDRESPONSABLE == Porte1Id))
            return;
        store.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 95, NOMROLE = "RefPorte1" });
        store.GetRepo<T_ROLE>().Items.Add(new T_ROLE { IDROLE = 96, NOMROLE = "RefPorte2" });
        store.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDDROITROLE = 9501, IDROLE = 95, IDDROIT = 1 });
        store.GetRepo<T_DROIT_ROLE>().Items.Add(new T_DROIT_ROLE { IDDROITROLE = 9502, IDROLE = 96, IDDROIT = 2 });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = Porte1Id, IDROLE = 95, LOGIN = "ref-porte1", NOMRESPONSABLE = "ref" });
        users.Add(new T_RESPONSABLE { IDRESPONSABLE = Porte2Id, IDROLE = 96, LOGIN = "ref-porte2", NOMRESPONSABLE = "ref" });
        store.GetRepo<T_BDD_AUTORISE>().Items.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = 9501, IDRESPONSABLE = Porte1Id, IDBDD = 3033 });
        store.GetRepo<T_BDD_AUTORISE>().Items.Add(new T_BDD_AUTORISE { IDBDDAUTORISE = 9502, IDRESPONSABLE = Porte2Id, IDBDD = 3033 });

        // Données fixes (aucune donnée de salarié réelle) pour que les états, l'analyse et les listes aient du contenu.
        var lundi = new DateTime(2025, 11, 3);
        var cartes = store.GetRepo<T_CARDPAIE>().Items;
        cartes.Add(new T_CARDPAIE { ID = 80001, SAGE_MATRICULE = "R001", SAGE_NOM = "NomA", SAGE_PRENOM = "PrenomA", DEPARTEMENT = "DEP1", SAGE_BDD = ApiFactory.SageName, POINTEUSE_BDD = "PTG" });
        cartes.Add(new T_CARDPAIE { ID = 80002, SAGE_MATRICULE = "R002", SAGE_NOM = "NomB", SAGE_PRENOM = "PrenomB", DEPARTEMENT = "DEP2", SAGE_BDD = ApiFactory.SageName, POINTEUSE_BDD = "PTG" });
        var plannings = store.GetRepo<T_PLANNING_SAL>().Items;
        for (var i = 0; i < 5; i++)
        {
            var jour = lundi.AddDays(i);
            plannings.Add(new T_PLANNING_SAL { ID = 80100 + i, IDSAL = 80001, DateP = jour, HA = jour.AddHours(8), HD = jour.AddHours(17), OffP = false, BDD_SAGE = ApiFactory.SageName, BDD_POINTEUSE = "PTG" });
            plannings.Add(new T_PLANNING_SAL { ID = 80200 + i, IDSAL = 80002, DateP = jour, HA = jour.AddHours(8), HD = jour.AddHours(17), OffP = false, BDD_SAGE = ApiFactory.SageName, BDD_POINTEUSE = "PTG" });
        }
        var pointages = store.GetRepo<T_POINTAGE>().Items;
        for (var i = 0; i < 5; i++)
        {
            pointages.Add(new T_POINTAGE { MATRICULE_SAGE = "R001", DATE_POINTAGE = lundi.AddDays(i), HEURE_POINTAGE = new TimeSpan(8, 5, 0), NOM_BDD_SAGE = ApiFactory.SageName, NOM_BDD_POINTEUSE = "PTG" });
            if (i != 2)
                pointages.Add(new T_POINTAGE { MATRICULE_SAGE = "R001", DATE_POINTAGE = lundi.AddDays(i), HEURE_POINTAGE = new TimeSpan(17, 0, 0), NOM_BDD_SAGE = ApiFactory.SageName, NOM_BDD_POINTEUSE = "PTG" });
        }
        var corrections = store.GetRepo<T_HEURECORRIGER>().Items;
        corrections.Add(new T_HEURECORRIGER { ID = 80301, MATRICULE_SAGE = "R001", DATE_POINTAGE_IN = lundi, HENTREE = new TimeSpan(8, 5, 0), HSORTIE = new TimeSpan(17, 0, 0), BDD_SAGE = ApiFactory.SageName });
        store.GetRepo<T_HEUREANOMALIE>().Items.Add(new T_HEUREANOMALIE { ID = 80401, MATRICULE_SAGE = "R001", DATE_POINTAGE_IN = lundi.AddDays(2), H1 = new TimeSpan(8, 5, 0), BDD_SAGE = ApiFactory.SageName });
        store.GetRepo<T_HS_VALIDER>().Items.Add(new T_HS_VALIDER { ID = 80501, BDD_SAGE = ApiFactory.SageName, MATRICULES = "R001", DATE_DEB = lundi, DATE_FIN = lundi.AddDays(6), HT = 44m, HS = 4m, VALIDATION_HS = true });
        store.GetRepo<T_HSExoImp>().Items.Add(new T_HSExoImp { OID = 80601, Matricule = "R001", PeriodeDebut = lundi, PeriodeFin = lundi.AddDays(6), EXO130 = 4m, BDD_SAGE = ApiFactory.SageName });
        store.GetRepo<T_FERIE>().Items.Add(new T_FERIE { DATE = lundi.AddDays(3), BDD_SAGE = ApiFactory.SageName });
    }

    private static string? Jeton(string profil) => profil switch
    {
        "admin" => ApiFactory.TokenFor("admin"),
        "porte1" => ApiFactory.Token(Porte1Id, 95, 1),
        "porte2" => ApiFactory.Token(Porte2Id, 96, 2),
        _ => null
    };

    [Fact]
    public async Task Record_the_reference_responses_of_every_parameterless_GET_route_for_three_profiles()
    {
        var dossier = Environment.GetEnvironmentVariable("SOFTTIME_REF_OUT");
        var source = _f.Services.GetRequiredService<EndpointDataSource>();
        var routes = source.Endpoints.OfType<RouteEndpoint>()
            .Where(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" }).Contains("GET"))
            .Select(e => e.RoutePattern.RawText?.TrimStart('/') ?? string.Empty)
            .Where(r => r.Length > 0 && !r.Contains('{') && r != "health" && !r.StartsWith("api/me/space", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
        Assert.True(routes.Count > 50, $"balayage trop court : {routes.Count} routes");

        var lignes = new List<string>();
        if (dossier is not null)
            Directory.CreateDirectory(Path.Combine(dossier, "corps"));
        foreach (var profil in new[] { "admin", "porte1", "porte2" })
            foreach (var route in routes)
            {
                var url = $"/{route}?from=2025-11-03&to=2025-11-07&page=1&pageSize=50";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Jeton(profil)}");
                req.Headers.TryAddWithoutValidation("X-Sage-Database", ApiFactory.SageName);
                req.Headers.TryAddWithoutValidation("X-Pointeuse-Database", "PTG");
                var resp = await _http.SendAsync(req);
                var octets = await resp.Content.ReadAsByteArrayAsync();
                var empreinte = Convert.ToHexString(SHA256.HashData(octets));
                lignes.Add($"{profil}\t{route}\t{(int)resp.StatusCode}\t{octets.Length}\t{empreinte}");
                if (dossier is not null)
                    await File.WriteAllBytesAsync(Path.Combine(dossier, "corps", $"{empreinte}.txt"), octets);
            }

        if (dossier is not null)
            await File.WriteAllLinesAsync(Path.Combine(dossier, "reponses.tsv"), lignes, new UTF8Encoding(false));
        Assert.Equal(routes.Count * 3, lignes.Count);
    }
}
