#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions.Landing;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services.Landing;

namespace SoftTime.Tests;

/// <summary>
/// Service d'atterrissage : choix du premier jour projeté, salariés retenus, totaux et horizon.
/// Planning 08:00–17:00 avec 1 h de pause, catégorie à 40 h, « aujourd'hui » jeudi 03/09/2026.
/// </summary>
public class WeekLandingServiceTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);
    private static readonly DateTime Mercredi = Lundi.AddDays(2);
    private static readonly DateTime Jeudi = Lundi.AddDays(3);

    private static readonly OvertimeWeekConfig Config = new(
        MultiPointage: true, HeuresSemaine: 40m, PauseCategorie: TimeSpan.FromMinutes(30));

    private static OvertimeDayInput Jour(DateTime date, string? sortie)
    {
        var paires = sortie is null
            ? Array.Empty<PunchPair>()
            : new[] { new PunchPair(date, TimeSpan.FromHours(8), date, TimeSpan.Parse(sortie)) };
        return new OvertimeDayInput(
            date, paires, TimeSpan.FromHours(8), TimeSpan.FromHours(17), TimeSpan.FromHours(1),
            CorrectionPresente: sortie is not null);
    }

    /// <summary>Salarié planifié du lundi au vendredi, pointé jusqu'à 19:00 les <paramref name="pointes"/> premiers jours.</summary>
    private static LandingEmployee Salarie(string matricule, int pointes, DateTime? lundi = null)
    {
        var debut = lundi ?? Lundi;
        var jours = Enumerable.Range(0, 7)
            .Select(i => i < 5
                ? Jour(debut.AddDays(i), i < pointes ? "19:00" : null)
                : new OvertimeDayInput(debut.AddDays(i), Array.Empty<PunchPair>()))
            .ToList();
        var card = new CardPaie { Id = 1, SageMatricule = matricule, SageNom = "Nom " + matricule, Departement = "Atelier" };
        return new LandingEmployee(card, Config, jours);
    }

    private static WeekLandingService Sut(FakeLoader loader, LandingOptions? options = null)
        => new(loader, new HorlogeFixe(Jeudi.AddHours(10)), Options.Create(options ?? new LandingOptions()),
            NullLogger<WeekLandingService>.Instance);

    [Fact]
    public async Task GetAsync_MiSemaine_ProjetteLesJoursAPartirDAujourdhui()
    {
        // Arrange : pointages jusqu'à mercredi, 3 jours de 10 h.
        var loader = new FakeLoader(new LandingContext(new[] { Salarie("M001", pointes: 3) }, Mercredi));

        // Act
        var resultat = await Sut(loader).GetAsync(Jeudi);

        // Assert
        resultat.Totaux.SemaineDebut.Should().Be(Lundi);
        resultat.DernierJourPointe.Should().Be(Mercredi);
        var ligne = resultat.Lignes.Should().ContainSingle().Subject;
        ligne.Matricule.Should().Be("M001");
        ligne.HeuresRealisees.Should().Be(30m);
        ligne.HsAtterrissage.Should().Be(6m);
        resultat.Totaux.EnHeuresSup.Should().Be(1);
        resultat.Totaux.HsAtterrissage.Should().Be(6m);
    }

    [Fact]
    public async Task GetAsync_ImportEnRetard_NeCompteJamaisUnJourNonImporteCommeUneAbsence()
    {
        // Arrange : dernier pointage traité le mardi ; le mercredi n'est pas encore importé.
        var loader = new FakeLoader(new LandingContext(new[] { Salarie("M001", pointes: 2) }, Lundi.AddDays(1)));

        // Act
        var resultat = await Sut(loader).GetAsync(Jeudi);

        // Assert : 20 h faites + 3 jours de 8 h planifiés = 44 h, soit 4 h supplémentaires.
        var ligne = resultat.Lignes.Should().ContainSingle().Subject;
        ligne.HeuresRealisees.Should().Be(20m);
        ligne.HeuresPlanifieesRestantes.Should().Be(24m);
        ligne.HsAtterrissage.Should().Be(4m);
    }

    [Fact]
    public async Task GetAsync_SalarieSansPointageNiPlanning_NApparaitPas()
    {
        // Arrange
        var inactif = new LandingEmployee(
            new CardPaie { Id = 2, SageMatricule = "M002" },
            Config,
            Enumerable.Range(0, 7).Select(i => new OvertimeDayInput(Lundi.AddDays(i), Array.Empty<PunchPair>())).ToList());
        var loader = new FakeLoader(new LandingContext(new[] { inactif }, Mercredi));

        // Act
        var resultat = await Sut(loader).GetAsync(Jeudi);

        // Assert
        resultat.Lignes.Should().BeEmpty();
        resultat.Totaux.Salaries.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_ChargeLaSemaineEtDeQuoiMesurerLEcartHabituel()
    {
        // Arrange
        var loader = new FakeLoader(new LandingContext(Array.Empty<LandingEmployee>(), null));

        // Act
        await Sut(loader, new LandingOptions { DriftWeeks = 4 }).GetAsync(Jeudi);

        // Assert : la semaine demandée et les cinq précédentes (quatre, plus une de marge pour un import en retard).
        loader.From.Should().Be(Lundi.AddDays(-35));
        loader.To.Should().Be(Lundi.AddDays(6));
    }

    [Fact]
    public async Task GetHorizonAsync_RendUneLigneParSemaineAPartirDeLaSemaineEnCours()
    {
        // Arrange : la semaine suivante est planifiée au contrat, sans pointage.
        var semaineSuivante = Salarie("M001", pointes: 0, Lundi.AddDays(7));
        var loader = new FakeLoader(new LandingContext(new[] { Salarie("M001", pointes: 3), semaineSuivante }, Mercredi));

        // Act
        var horizon = await Sut(loader, new LandingOptions { HorizonWeeks = 3 }).GetHorizonAsync();

        // Assert
        horizon.Select(w => w.SemaineDebut).Should().Equal(Lundi, Lundi.AddDays(7), Lundi.AddDays(14));
        horizon[0].HsAtterrissage.Should().Be(6m);
        horizon[1].Salaries.Should().Be(1);
        horizon[1].HsAtterrissage.Should().Be(0m);
        horizon[2].Salaries.Should().Be(0);
    }

    private sealed class FakeLoader : ILandingContextLoader
    {
        private readonly LandingContext _context;

        public FakeLoader(LandingContext context) => _context = context;

        public DateTime From { get; private set; }
        public DateTime To { get; private set; }

        public Task<LandingContext> LoadAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            (From, To) = (from, to);
            return Task.FromResult(_context);
        }
    }

    private sealed class HorlogeFixe : TimeProvider
    {
        private readonly DateTime _maintenant;

        public HorlogeFixe(DateTime maintenant) => _maintenant = maintenant;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(_maintenant, TimeSpan.Zero);
    }
}
