using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Services;

namespace SoftTime.Tests;

/// <summary>
/// Tests de la chaîne de validation semaine et de la ventilation/exonération, sur le UoW en mémoire
/// (les règles pures sont couvertes par <see cref="OvertimeCalculatorTests"/>).
/// </summary>
public class OvertimeServiceTests
{
    private static readonly DateTime Lundi = new(2026, 8, 31);
    private static readonly DateTime Dimanche = Lundi.AddDays(6);

    private static readonly TimeSpan Huit = new(8, 0, 0);
    private static readonly TimeSpan DixSept = new(17, 0, 0);
    private static readonly TimeSpan DixHuit = new(18, 0, 0);

    private sealed class Contexte
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public FakeExternalSourceService Source { get; } = new();
        public OvertimeServices Service { get; }

        public List<HeureCorrigee> Corrections => Uow.Get<HeureCorrigee>().Items;
        public List<HsValidee> Semaines => Uow.Get<HsValidee>().Items;
        public List<HsSemaine> Repartition => Uow.Get<HsSemaine>().Items;
        public List<HsExoImp> Exo => Uow.Get<HsExoImp>().Items;

        public Contexte(bool multi = true)
        {
            Uow.Get<BddSage>().Items.Add(new BddSage { Id = 1, NomBd = "SAGE_TEST" });
            Uow.Get<ParametrePointeuse>().Items.Add(new ParametrePointeuse { MultiPoint = multi });
            Uow.Get<CardPaie>().Items.Add(new CardPaie
            {
                Id = 1,
                SageMatricule = "001",
                SageBdd = "SAGE_TEST",
                PointeuseBdd = "PTE_TEST"
            });
            Uow.Get<Categorie>().Items.Add(new Categorie
            {
                IdCategorie = 10,
                Intitule = "Sédentaire",
                HeureSemaine = 40m,
                Pause = TimeSpan.FromMinutes(30),
                BddSage = "SAGE_TEST"
            });
            Uow.Get<CategorieSalarie>().Items.Add(new CategorieSalarie
            {
                Id = 1,
                MatriculeSage = 1,
                IdCateg = 10,
                BddSage = "SAGE_TEST"
            });
            Uow.Get<Majoration>().Items.AddRange(new[]
            {
                new Majoration { Mojoration = "NUIT", Cotation = 1m },
                new Majoration { Mojoration = "DIMANCHE", Cotation = 2m },
                new Majoration { Mojoration = "FERIE", Cotation = 3m },
            });

            var tenant = new TenantConnectionService(Uow, new FakeCompanyContext(), new FakeCurrentUser(), SoftTime.Tests.Http.TestRights.Service(Uow), Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantConnectionService>.Instance);
            Service = new OvertimeServices(
                Uow,
                tenant,
                Source,
                new PayrollWriterResolver(Array.Empty<IPayrollWriter>()),
                new HistoryArchiveService(Uow),
                new SageLeaveCalendar(tenant, Source));
        }

        public void Planning(DateTime jour, TimeSpan? pause, TimeSpan? ha = null, TimeSpan? hd = null)
            => Uow.Get<PlanningSalarie>().Items.Add(new PlanningSalarie
            {
                IdSal = 1,
                DateP = jour.Date,
                Ha = jour.Date + (ha ?? Huit),
                Hd = jour.Date + (hd ?? DixSept),
                Pause = pause,
                OffP = false,
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST"
            });

        public void Paire(DateTime jour, TimeSpan entree, TimeSpan sortie)
            => Corrections.Add(new HeureCorrigee
            {
                BddSage = "SAGE_TEST",
                BddPointeuse = "PTE_TEST",
                MatriculeSage = "001",
                DatePointageIn = jour.Date,
                DatePointageOut = jour.Date,
                HEntree = entree,
                HSortie = sortie,
                ValiderCorrection = true,
                Origine = "AUTO"
            });

        public void SemaineValidee(DateTime debut, decimal hs, bool validee = true)
            => Semaines.Add(new HsValidee
            {
                BddSage = "SAGE_TEST",
                BddPointage = "PTE_TEST",
                Matricules = "001",
                DateDeb = debut.Date,
                DateFin = debut.Date.AddDays(6),
                Hs = hs,
                ValidationHs = validee
            });

        public Task<IReadOnlyList<WeeklyHsDto>> Apercu(DateTime from, DateTime to)
            => Service.PreviewWeekAsync(new PeriodRequest(null, null, from, to));

        public Task<PagedResult<WeeklyHsDto>> ApercuPagine(DateTime from, DateTime to)
            => Service.PreviewWeekPagedAsync(new PeriodRequest(null, null, from, to), 1, 50);
    }

    private static void SemaineOuvree(Contexte contexte, int heuresSortie = 17, TimeSpan? pause = null)
    {
        for (var i = 0; i < 5; i++)
        {
            var jour = Lundi.AddDays(i);
            contexte.Planning(jour, pause ?? TimeSpan.Zero);
            contexte.Paire(jour, Huit, heuresSortie == 17 ? DixSept : DixHuit);
        }
    }

    // ------------------------------------------------------------------ calcul hebdomadaire

    [Fact]
    public async Task Apercu_MultiPointage_CalculeLesHSDeLaSemaine()
    {
        // Arrange
        var sut = new Contexte();
        SemaineOuvree(sut);

        // Act
        var lignes = await sut.Apercu(Lundi, Dimanche);

        // Assert
        var ligne = lignes.Should().ContainSingle().Which;
        ligne.Ht.Should().Be(45m);
        ligne.Hs.Should().Be(5m);
        var persistee = sut.Semaines.Should().ContainSingle().Which;
        persistee.Ht.Should().Be(45m);
        persistee.ValidationHs.Should().BeFalse();
    }

    [Fact]
    public async Task Apercu_MonoPointage_UtiliseLesDepassementsJournaliers()
    {
        // Arrange
        var sut = new Contexte(multi: false);
        SemaineOuvree(sut, heuresSortie: 18, pause: TimeSpan.FromMinutes(30));

        // Act
        var lignes = await sut.Apercu(Lundi, Dimanche);

        // Assert
        var ligne = lignes.Should().ContainSingle().Which;
        ligne.Ht.Should().Be(47.5m);
        ligne.Hs.Should().Be(5m);
    }

    [Fact]
    public async Task Apercu_Conge_CrediteLaJourneeSansAbsence()
    {
        // Arrange
        var sut = new Contexte();
        sut.Source.Employees.Add(new ExternalEmployee("001", 1, null, null, null, false));
        sut.Source.Events.Add(new ExternalEvent("0100", "Congé"));
        sut.Source.EmployeeEvents.Add(new ExternalEmployeeEvent(1, "0100", Lundi, Lundi, false, false));
        sut.Planning(Lundi, TimeSpan.Zero);
        for (var i = 1; i < 5; i++)
        {
            var jour = Lundi.AddDays(i);
            sut.Planning(jour, TimeSpan.Zero);
            sut.Paire(jour, Huit, DixSept);
        }

        // Act
        var lignes = await sut.Apercu(Lundi, Dimanche);

        // Assert
        var ligne = lignes.Should().ContainSingle().Which;
        ligne.Ht.Should().Be(44m);
        ligne.Hs.Should().Be(4m);
        ligne.Absence.Should().Be(0m);
    }

    [Fact]
    public async Task PreviewWeekAsync_SalarieSansPointage_DonneUneLigneParSalarie()
    {
        // Arrange
        var sut = new Contexte();

        // Act
        var lignes = await sut.Apercu(Lundi, Dimanche);

        // Assert
        var ligne = lignes.Should().ContainSingle().Which;
        ligne.Matricule.Should().Be("001");
        ligne.Ht.Should().Be(0m);
        ligne.Hs.Should().Be(0m);
    }

    // ------------------------------------------------------------------ validation / annulation

    [Fact]
    public async Task Validation_Puis_Annulation_RouvreLaSemaine()
    {
        // Arrange
        var contexte = new Contexte();
        SemaineOuvree(contexte);
        var request = new WeeklyValidationRequest(null, null, Lundi, Dimanche, new[] { "001" });

        // Act
        await contexte.Service.ValidateWeekAsync(request, true);

        // Assert
        var ligne = contexte.Semaines.Should().ContainSingle().Which;
        ligne.ValidationHs.Should().BeTrue();
        contexte.Corrections.Should().AllSatisfy(h => h.ValiderHs.Should().BeTrue());
        // La semaine figée reste visible (pour pouvoir annuler la validation) et n'est pas recalculée.
        var pagine = await contexte.ApercuPagine(Lundi, Dimanche);
        pagine.Items.Should().ContainSingle().Which.Validated.Should().BeTrue();
        await contexte.Service.ValidateWeekAsync(request, false);
        contexte.Semaines.Should().ContainSingle().Which.ValidationHs.Should().BeFalse();
        contexte.Corrections.Should().AllSatisfy(h => h.ValiderHs.Should().BeFalse());
    }

    [Fact]
    public async Task ValidateWeekAsync_SansSelection_NeFaitRien()
    {
        // Arrange
        var contexte = new Contexte();
        SemaineOuvree(contexte);

        // Act
        await contexte.Service.ValidateWeekAsync(
            new WeeklyValidationRequest(null, null, Lundi, Dimanche, Array.Empty<string>()), true);

        // Assert
        contexte.Semaines.Should().BeEmpty();
        contexte.Corrections.Should().AllSatisfy(h => h.ValiderHs.Should().BeNull());
    }

    // ------------------------------------------------------------------ ventilation / exonération

    [Fact]
    public async Task CalculateExoImpoAsync_SemainesMixtes_NeRegroupeQueLesValideesEtReconstruitLaRepartition()
    {
        // Arrange
        var contexte = new Contexte();
        var debut = Lundi.AddDays(-7);
        var fin = Lundi.AddDays(13);
        contexte.SemaineValidee(debut, 12);
        contexte.SemaineValidee(Lundi, 14);
        contexte.SemaineValidee(Lundi.AddDays(7), 20, validee: false);

        // Act
        var exo = await contexte.Service.CalculateExoImpoAsync(new PeriodRequest(null, null, debut, fin));

        // Assert
        var ligne = exo.Should().ContainSingle().Which;
        ligne.Exo130.Should().Be(16m);
        ligne.Exo150.Should().Be(4m);
        ligne.I130.Should().Be(0m);
        ligne.I150.Should().Be(6m);
        contexte.Repartition.Count.Should().Be(2);
        contexte.Repartition[0].Semaine.Should().Be(1);
        contexte.Repartition[0].Hs.Should().Be(12m);
        contexte.Repartition[0].Hs1.Should().Be(8m);
        contexte.Repartition[0].Hs2.Should().Be(4m);
        contexte.Repartition[1].Semaine.Should().Be(2);
        contexte.Repartition[1].Hs1.Should().Be(8m);
        contexte.Repartition[1].Hs2.Should().Be(6m);
    }

    [Fact]
    public async Task CalculateExoImpoAsync_Reexecution_ReconstruitLaRepartition()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.SemaineValidee(Lundi, 10);
        var request = new PeriodRequest(null, null, Lundi, Dimanche);
        await contexte.Service.CalculateExoImpoAsync(request);
        contexte.Semaines.Single().Hs = 6;

        // Act
        await contexte.Service.CalculateExoImpoAsync(request);

        // Assert
        var semaine = contexte.Repartition.Should().ContainSingle().Which;
        semaine.Hs.Should().Be(6m);
        semaine.Hs1.Should().Be(6m);
    }

    [Fact]
    public async Task CalculateExoImpoAsync_AbsenceSurLaPeriode_LaReporte()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.Planning(Lundi, TimeSpan.FromMinutes(30));

        // Act
        var exo = await contexte.Service.CalculateExoImpoAsync(new PeriodRequest(null, null, Lundi, Dimanche));

        // Assert
        var ligne = exo.Should().ContainSingle().Which;
        ligne.Absence.Should().Be(8.5m);
        ligne.Exo130.Should().Be(0m);
        contexte.Repartition.Should().BeEmpty();
        contexte.Exo.Should().ContainSingle();
    }

    [Fact]
    public async Task ExoImpo_PeriodeDemarrantEnMilieuDeSemaine_InclutLaSemaineEnCours()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.SemaineValidee(Lundi, 10);

        // Act
        // Période 01/09 → 30/09 : la semaine du 31/08 déborde sur le mois et doit être prise en compte.
        var exo = await contexte.Service.CalculateExoImpoAsync(
            new PeriodRequest(null, null, Lundi.AddDays(1), new DateTime(2026, 9, 30)));

        // Assert
        var ligne = exo.Should().ContainSingle().Which;
        ligne.Exo130.Should().Be(8m);
        ligne.Exo150.Should().Be(2m);
    }

    [Fact]
    public async Task ExoImpo_SemaineAChevalSurDeuxMois_NEstPayeeQuUneFois()
    {
        // Arrange
        var contexte = new Contexte();
        // Semaine du lundi 31/08 au dimanche 06/09 : elle appartient à septembre (son dimanche).
        contexte.SemaineValidee(Lundi, 10);
        var aout = await contexte.Service.CalculateExoImpoAsync(
            new PeriodRequest(null, null, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31)));

        // Act
        var septembre = await contexte.Service.CalculateExoImpoAsync(
            new PeriodRequest(null, null, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)));

        // Assert
        aout.Should().BeEmpty();
        var ligne = septembre.Should().ContainSingle().Which;
        ligne.Exo130.Should().Be(8m);
        ligne.Exo150.Should().Be(2m);
    }

    [Fact]
    public async Task ExoImpo_AbsenceArbitreeSansHeures_EstComptee()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.Planning(Lundi, TimeSpan.FromMinutes(30));
        // Anomalie d'absence validée : la journée porte une ligne d'absence, sans heure.
        contexte.Corrections.Add(new HeureCorrigee
        {
            BddSage = "SAGE_TEST",
            MatriculeSage = "001",
            DatePointageIn = Lundi,
            DatePointageOut = Lundi,
            AbsenceAm = true,
            AbsencePm = true,
            Origine = "MANUEL"
        });

        // Act
        var exo = await contexte.Service.CalculateExoImpoAsync(new PeriodRequest(null, null, Lundi, Dimanche));

        // Assert
        exo.Should().ContainSingle().Which.Absence.Should().Be(8.5m);
    }

    [Fact]
    public async Task Validation_SemaineDejaCalculeeEnExoImpo_NePeutEtreNiAnnuleeNiPurgee()
    {
        // Arrange
        var contexte = new Contexte();
        contexte.SemaineValidee(Lundi, 10);
        await contexte.Service.CalculateExoImpoAsync(
            new PeriodRequest(null, null, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)));
        var annulation = new WeeklyValidationRequest(null, null, Lundi, Dimanche, new[] { "001" });

        // Act & Assert
        await FluentActions.Awaiting(() => contexte.Service.ValidateWeekAsync(annulation, false)).Should().ThrowExactlyAsync<ConflictException>();
        await FluentActions.Awaiting(() => contexte.Service.PurgeValidationAsync(new PeriodRequest(null, null, Lundi, Dimanche))).Should().ThrowExactlyAsync<ConflictException>();
        contexte.Semaines.Should().ContainSingle().Which.ValidationHs.Should().BeTrue();
    }

    // ------------------------------------------------------------------ détail

    [Fact]
    public async Task DetailAsync_SemainesValidees_ExposeLeursBornesReelles()
    {
        // Arrange
        // Période 01/05 → 31/05/2025 : la première semaine validée commence avant le mois (28/04).
        var contexte = new Contexte();
        var debutPeriode = new DateTime(2025, 5, 1);
        var finPeriode = new DateTime(2025, 5, 31);
        var semaine1 = new DateTime(2025, 4, 28);
        var semaine2 = new DateTime(2025, 5, 5);
        contexte.SemaineValidee(semaine1, 0m);
        contexte.SemaineValidee(semaine2, 11.21m);
        await contexte.Service.CalculateExoImpoAsync(new PeriodRequest(null, null, debutPeriode, finPeriode));

        // Act
        var detail = await contexte.Service.DetailAsync("001", debutPeriode, finPeriode);

        // Assert
        detail.Semaines.Count.Should().Be(2);
        detail.Semaines[0].Semaine!.Value.Should().Be(1);
        detail.Semaines[0].Debut!.Value.Should().Be(semaine1);
        detail.Semaines[0].Fin!.Value.Should().Be(semaine1.AddDays(6));
        detail.Semaines[1].Semaine!.Value.Should().Be(2);
        detail.Semaines[1].Debut!.Value.Should().Be(semaine2);
        detail.Semaines[1].Fin!.Value.Should().Be(semaine2.AddDays(6));
        detail.Semaines[1].Hs.Should().Be(11.21m);
        detail.Semaines[1].Hs1.Should().Be(8m);
        detail.Semaines[1].Hs2.Should().Be(3.21m);
    }

    [Fact]
    public async Task DetailAsync_SemainesNonConcordantes_SeReplieSurLaPeriode()
    {
        // Arrange
        // Ligne héritée (aucune semaine validée correspondante) : on affiche la période stockée.
        var contexte = new Contexte();
        var debut = new DateTime(2025, 5, 1);
        var fin = new DateTime(2025, 5, 31);
        contexte.Repartition.Add(new HsSemaine
        {
            Matricule = "001",
            PeriodeDebut = debut,
            PeriodeFin = fin,
            Semaine = 2,
            Hs = 3m,
            Hs1 = 3m,
            Hs2 = 0m,
            BddSage = "SAGE_TEST",
            BddPointeuse = "PTE_TEST"
        });

        // Act
        var detail = await contexte.Service.DetailAsync("001", debut, fin);

        // Assert
        var semaine = detail.Semaines.Should().ContainSingle().Which;
        semaine.Semaine!.Value.Should().Be(2);
        semaine.Debut!.Value.Should().Be(debut);
        semaine.Fin!.Value.Should().Be(fin);
    }
}
