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

    private static void Seed(InMemoryUnitOfWork store)
    {
        var users = store.Get<Responsable>().Items;
        if (users.Any(u => u.IdResponsable == Porte1Id))
            return;
        store.Get<Role>().Items.Add(new Role { IdRole = 95, NomRole = "RefPorte1" });
        store.Get<Role>().Items.Add(new Role { IdRole = 96, NomRole = "RefPorte2" });
        store.Get<DroitRole>().Items.Add(new DroitRole { IdDroitRole = 9501, IdRole = 95, IdDroit = 1 });
        store.Get<DroitRole>().Items.Add(new DroitRole { IdDroitRole = 9502, IdRole = 96, IdDroit = 2 });
        users.Add(new Responsable { IdResponsable = Porte1Id, IdRole = 95, Login = "ref-porte1", NomResponsable = "ref" });
        users.Add(new Responsable { IdResponsable = Porte2Id, IdRole = 96, Login = "ref-porte2", NomResponsable = "ref" });
        store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = 9501, IdResponsable = Porte1Id, IdBdd = 3033 });
        store.Get<BddAutorisee>().Items.Add(new BddAutorisee { IdBddAutorise = 9502, IdResponsable = Porte2Id, IdBdd = 3033 });

        // Données fixes (aucune donnée de salarié réelle) pour que les états, l'analyse et les listes aient du contenu.
        var lundi = new DateTime(2025, 11, 3);
        var cartes = store.Get<CardPaie>().Items;
        cartes.Add(new CardPaie { Id = 80001, SageMatricule = "R001", SageNom = "NomA", SagePrenom = "PrenomA", Departement = "DEP1", SageBdd = ApiFactory.SageName, PointeuseBdd = "PTG" });
        cartes.Add(new CardPaie { Id = 80002, SageMatricule = "R002", SageNom = "NomB", SagePrenom = "PrenomB", Departement = "DEP2", SageBdd = ApiFactory.SageName, PointeuseBdd = "PTG" });
        var plannings = store.Get<PlanningSalarie>().Items;
        for (var i = 0; i < 5; i++)
        {
            var jour = lundi.AddDays(i);
            plannings.Add(new PlanningSalarie { Id = 80100 + i, IdSal = 80001, DateP = jour, Ha = jour.AddHours(8), Hd = jour.AddHours(17), OffP = false, BddSage = ApiFactory.SageName, BddPointeuse = "PTG" });
            plannings.Add(new PlanningSalarie { Id = 80200 + i, IdSal = 80002, DateP = jour, Ha = jour.AddHours(8), Hd = jour.AddHours(17), OffP = false, BddSage = ApiFactory.SageName, BddPointeuse = "PTG" });
        }
        var pointages = store.Get<Pointage>().Items;
        for (var i = 0; i < 5; i++)
        {
            pointages.Add(new Pointage { MatriculeSage = "R001", DatePointage = lundi.AddDays(i), HeurePointage = new TimeSpan(8, 5, 0), NomBddSage = ApiFactory.SageName, NomBddPointeuse = "PTG" });
            if (i != 2)
                pointages.Add(new Pointage { MatriculeSage = "R001", DatePointage = lundi.AddDays(i), HeurePointage = new TimeSpan(17, 0, 0), NomBddSage = ApiFactory.SageName, NomBddPointeuse = "PTG" });
        }
        var corrections = store.Get<HeureCorrigee>().Items;
        corrections.Add(new HeureCorrigee { Id = 80301, MatriculeSage = "R001", DatePointageIn = lundi, HEntree = new TimeSpan(8, 5, 0), HSortie = new TimeSpan(17, 0, 0), BddSage = ApiFactory.SageName });
        store.Get<HeureAnomalie>().Items.Add(new HeureAnomalie { Id = 80401, MatriculeSage = "R001", DatePointageIn = lundi.AddDays(2), H1 = new TimeSpan(8, 5, 0), BddSage = ApiFactory.SageName });
        store.Get<HsValidee>().Items.Add(new HsValidee { Id = 80501, BddSage = ApiFactory.SageName, Matricules = "R001", DateDeb = lundi, DateFin = lundi.AddDays(6), Ht = 44m, Hs = 4m, ValidationHs = true });
        store.Get<HsExoImp>().Items.Add(new HsExoImp { Oid = 80601, Matricule = "R001", PeriodeDebut = lundi, PeriodeFin = lundi.AddDays(6), Exo130 = 4m, BddSage = ApiFactory.SageName });
        store.Get<JourFerie>().Items.Add(new JourFerie { Date = lundi.AddDays(3), BddSage = ApiFactory.SageName });
    }

    private static string? Jeton(string profil) => profil switch
    {
        "admin" => ApiFactory.TokenFor("admin"),
        "porte1" => ApiFactory.Token(Porte1Id, 95, 1),
        "porte2" => ApiFactory.Token(Porte2Id, 96, 2),
        _ => null
    };

    [Fact]
    public async Task ParameterlessGetRoutes_ThreeProfiles_AreRecordedAsReferenceResponses()
    {
        // Arrange
        var dossier = Environment.GetEnvironmentVariable("SOFTTIME_REF_OUT");
        var source = _f.Services.GetRequiredService<EndpointDataSource>();
        var routes = source.Endpoints.OfType<RouteEndpoint>()
            .Where(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" }).Contains("GET"))
            .Select(e => e.RoutePattern.RawText?.TrimStart('/') ?? string.Empty)
            .Where(r => r.Length > 0 && !r.Contains('{') && r != "health" && !r.StartsWith("api/v1/me/space", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
        routes.Count.Should().BeGreaterThan(50, "le balayage doit couvrir toutes les routes");

        // Act
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
        // Assert
        lignes.Count.Should().Be(routes.Count * 3);
    }
}
