using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using SoftTime.Application.Abstractions.Closing;
using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.Permissions;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Abstractions.Settings;
using SoftTime.Application.DTOs;
using SoftTime.Application.Security;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.SchemaMigrations;
using SoftTime.Infrastructure.Services.Closing;
using SoftTime.Infrastructure.Services.Extras;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

/// <summary>
/// Lot D5 : domaines CANTINE et FRAIS dans l'instantané et la clôture. Marqueur et instantané à l'envoi des cumuls, empreinte
/// au format 2 (format 1 des heures inchangé), activation par base, clôture exigeant chaque domaine activé ou déjà envoyé,
/// état par domaine, migration 011.
/// </summary>
public class PayrollClosureD5Tests
{
    private const string Base = "SFOI_PAIE2025";
    private static readonly DateTime Debut = new(2026, 8, 17);
    private static readonly DateTime Fin = new(2026, 8, 23);
    private static readonly DateTimeOffset Aujourdhui = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private static readonly PayrollSnapshotLine[] LignesCantine =
    [
        new("04634", 103, PayrollDomains.Cantine, "CANTINE_CODE", PayrollDomains.UniteNombre, 4m, false),
        new("00125", 7, PayrollDomains.Cantine, "CANTINE_CODE", PayrollDomains.UniteNombre, 2m, false),
    ];

    // =====================================================================================================
    // Empreinte : format 1 des heures inchangé, format 2 des domaines
    // =====================================================================================================

    [Fact]
    public void Fingerprint_Hours_Format1_IsUnchanged()
    {
        var heures = new[] { new PayrollSnapshotLine("04634", 103, "Exo130", "HS01", "H", 1.5m, false) };

        PayrollSnapshotFingerprint.Canonique(1, "B", Debut, Fin, "standard", heures)
            .Should().Be("SOFTTIME-PAIE-INSTANTANE|1|B|2026-08-17|2026-08-23|STANDARD\n04634|103|Exo130|HS01|H|1.5000|0");
        var v1 = PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD", heures);
        PayrollSnapshotFingerprint.Calculer(1, PayrollDomains.Heures, "B", Debut, Fin, "STANDARD", heures).Should().Be(v1);
        PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Heures, "B", Debut, Fin, "STANDARD", heures).Should().Be(v1);
        PayrollDomains.VersionEmpreinte(PayrollDomains.Heures).Should().Be(1);
    }

    [Fact]
    public void Fingerprint_Format2_NamesTheDomain_AndKeepsTheFormat1Lines()
    {
        PayrollSnapshotFingerprint.Canonique(2, PayrollDomains.Cantine, " B ", Debut, Fin, "standard", LignesCantine)
            .Should().Be("SOFTTIME-PAIE-INSTANTANE|2|CANTINE|B|2026-08-17|2026-08-23|STANDARD\n"
                         + "00125|7|CANTINE|CANTINE_CODE|N|2.0000|0\n"
                         + "04634|103|CANTINE|CANTINE_CODE|N|4.0000|0");
        PayrollDomains.VersionEmpreinte(PayrollDomains.Cantine).Should().Be(2);
        PayrollDomains.VersionEmpreinte(PayrollDomains.Frais).Should().Be(2);
    }

    [Fact]
    public void Fingerprint_Format2_IsDeterministic_AndSensitive()
    {
        var reference = PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Cantine, Base, Debut, Fin, "STANDARD", LignesCantine);

        reference.Should().MatchRegex("^[0-9a-f]{64}$");
        PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Cantine, Base, Debut, Fin, "STANDARD", LignesCantine.Reverse())
            .Should().Be(reference, "l'ordre des lignes ne compte pas");
        var variantes = new[]
        {
            PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Frais, Base, Debut, Fin, "STANDARD", LignesCantine),
            PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Cantine, Base, Debut, Fin, "STANDARD",
                [LignesCantine[0] with { Valeur = 5m }, LignesCantine[1]]),
            PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Cantine, Base, Debut, Fin, "STANDARD", [LignesCantine[0]]),
            PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Cantine, Base, Debut, Fin.AddDays(1), "STANDARD", LignesCantine),
            PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Cantine, Base + "X", Debut, Fin, "STANDARD", LignesCantine),
        };
        variantes.Should().OnlyHaveUniqueItems().And.NotContain(reference);
    }

    [Fact]
    public void Fingerprint_FormatAndDomainMustMatch()
    {
        FluentActions.Invoking(() => PayrollSnapshotFingerprint.Calculer(2, PayrollDomains.Heures, "B", Debut, Fin, "STANDARD", LignesCantine))
            .Should().Throw<DomainException>().WithMessage("Format d'empreinte 2 incompatible avec le domaine HEURES.");
        FluentActions.Invoking(() => PayrollSnapshotFingerprint.Calculer(1, PayrollDomains.Cantine, "B", Debut, Fin, "STANDARD", LignesCantine))
            .Should().Throw<DomainException>().WithMessage("Format d'empreinte 1 incompatible avec le domaine CANTINE.");
        FluentActions.Invoking(() => PayrollSnapshotFingerprint.Calculer(3, PayrollDomains.Frais, "B", Debut, Fin, "STANDARD", LignesCantine))
            .Should().Throw<DomainException>().WithMessage("Format d'empreinte d'instantané inconnu : 3.");
    }

    [Fact]
    public void Verify_DomainSnapshot_DetectsAChangedDomain()
    {
        var entete = new PaieInstantane
        {
            Id = 1, BddSage = Base, PeriodeDebut = Debut, PeriodeFin = Fin, TypeBase = "STANDARD", Domaine = PayrollDomains.Cantine,
            VersionFormat = 2, NbLignes = 2,
            Empreinte = PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Cantine, Base, Debut, Fin, "STANDARD", LignesCantine)
        };

        PayrollSnapshotService.Verifier(entete, LignesCantine).Integre.Should().BeTrue();
        entete.Domaine = PayrollDomains.Frais;
        PayrollSnapshotService.Verifier(entete, LignesCantine).Integre.Should().BeFalse("le domaine entre dans l'empreinte du format 2");
        entete.Domaine = PayrollDomains.Heures;
        PayrollSnapshotService.Verifier(entete, LignesCantine).Integre.Should().BeFalse("format 2 et domaine HEURES sont incompatibles");
    }

    // =====================================================================================================
    // Envoi de la cantine et des frais : marqueur, instantané, statuts
    // =====================================================================================================

    private sealed class Envoi
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public Mock<ISagePayrollWriter> Writer { get; } = new();
        public Mock<IExternalSourceService> Sage { get; } = new();
        public PayrollSendLog SendLog { get; }
        public List<string?> StatutsVusALaPremiereEcriture { get; } = new();

        public Envoi()
        {
            SendLog = new PayrollSendLog(new ScopeFactory(Uow), NullLogger<PayrollSendLog>.Instance);
            Sage.Setup(s => s.GetConstantsAsync(It.IsAny<BddSage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new ExternalConstant("CANTINE_CODE", 41, "Cantine"), new ExternalConstant("HA06", 42, "Frais")]);
            Sage.Setup(s => s.GetEmployeesAsync(It.IsAny<BddSage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new ExternalEmployee("04634", 103, "A", "B", null, false), new ExternalEmployee("00125", 7, "C", "D", null, false),
                    new ExternalEmployee("00999", 9, "E", "F", null, false)]);
            // Salarié 9 : aucun cumul dans T_CUMSAL (mise à jour sans ligne) ; les autres : une ligne modifiée.
            Writer.Setup(w => w.UpdateCumulAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, int numSal, short _, decimal _, CancellationToken _) =>
                {
                    StatutsVusALaPremiereEcriture.Add(Uow.Get<PaieEnvoi>().Items.SingleOrDefault()?.Statut);
                    return numSal == 9 ? 0 : 1;
                });
        }

        public PayrollCumulSync Sut() => new(Uow, Tenant(), Sage.Object, Writer.Object, closures: null, SendLog, new TestCurrentUser(42, 1));

        public void Cantine(string matricule, int total) => Uow.Get<Cantine>().Items.Add(new Cantine
        {
            IdCantine = Uow.Get<Cantine>().Items.Count + 1, NomBddSage = Base, MatriculeSage = matricule, DateDeb = Debut, DateFin = Fin, TotalCantine = total
        });

        public void Frais(string matricule, int total) => Uow.Get<Frais>().Items.Add(new Frais
        {
            IdPointage = Uow.Get<Frais>().Items.Count + 1, NomBddSage = Base, MatriculeSage = matricule, DateDeb = Debut, DateFin = Fin, TotalPoint = total
        });
    }

    private static PeriodRequest Periode(string? de = null, string? a = null) => new(de, a, Debut, Fin);

    [Fact]
    public async Task SyncCanteen_MarksTheSendBeforeWriting_ThenFreezesExactlyTheWrittenLines()
    {
        var e = new Envoi();
        e.Cantine("04634", 4);
        e.Cantine("00125", 2);
        e.Cantine("00999", 3); // sans cumul : compté, pas figé
        e.Cantine("77777", 1); // absent de SAGE : ni écrit, ni figé

        var resultat = await e.Sut().SyncExtrasAsync(Periode(), "CANTINE_CODE", cantine: true, CancellationToken.None);

        resultat.Message.Should().Be("3 salarié(s) synchronisé(s), 2 cumul(s) mis à jour. 1 matricule(s) absent(s) de SAGE : 77777. "
                                     + "1 salarié(s) sans ligne de cumul CANTINE_CODE (opérande 41).", "le message de l'envoi ne change pas");
        e.StatutsVusALaPremiereEcriture.Should().NotBeEmpty().And.OnlyContain(s => s == "EN_COURS", "le marqueur est posé avant la première écriture");
        var envoi = e.Uow.Get<PaieEnvoi>().Items.Single();
        envoi.Domaine.Should().Be(PayrollDomains.Cantine);
        envoi.Statut.Should().Be("SUCCES");
        envoi.Lignes.Should().Be(2);
        envoi.TypeBase.Should().Be("STANDARD");
        envoi.EnvoyePar.Should().Be("u42");
        (envoi.PeriodeDebut, envoi.PeriodeFin).Should().Be((Debut, Fin));
        var entete = e.Uow.Get<PaieInstantane>().Items.Single();
        entete.Domaine.Should().Be(PayrollDomains.Cantine);
        entete.VersionFormat.Should().Be(2);
        entete.AvecFiltre.Should().BeFalse();
        entete.NbLignes.Should().Be(2);
        entete.NbSansLigne.Should().Be(1);
        var lignes = e.Uow.Get<PaieInstantaneLigne>().Items.OrderBy(l => l.Matricule).ToList();
        lignes.Select(l => (l.Matricule, l.NumSal, l.Variable, l.Destination, l.Unite, l.Valeur, l.RemiseAZero)).Should().Equal(
            ("00125", (int?)7, "CANTINE", "CANTINE_CODE", "N", 2m, false),
            ("04634", (int?)103, "CANTINE", "CANTINE_CODE", "N", 4m, false));
        entete.Empreinte.Should().Be(PayrollSnapshotFingerprint.CalculerDomaine(PayrollDomains.Cantine, Base, Debut, Fin, "STANDARD", LignesCantine));
    }

    [Fact]
    public async Task SyncTravel_WithAMatriculeRange_IsAFilteredFraisSend()
    {
        var e = new Envoi();
        e.Frais("04634", 6);
        e.Frais("00125", 1);

        await e.Sut().SyncExtrasAsync(Periode("04000", "05000"), "HA06", cantine: false, CancellationToken.None);

        var entete = e.Uow.Get<PaieInstantane>().Items.Single();
        entete.Domaine.Should().Be(PayrollDomains.Frais);
        entete.AvecFiltre.Should().BeTrue();
        (entete.MatriculeDe, entete.MatriculeA, entete.Departement).Should().Be(("04000", "05000", (string?)null));
        e.Uow.Get<PaieInstantaneLigne>().Items.Should().ContainSingle()
            .Which.Should().Match<PaieInstantaneLigne>(l => l.Matricule == "04634" && l.Destination == "HA06" && l.Valeur == 6m && l.Variable == "FRAIS");
    }

    [Fact]
    public async Task Sync_WriteFailure_LeavesAnEchecSend_WithoutSnapshot()
    {
        var e = new Envoi();
        e.Cantine("04634", 4);
        e.Writer.Setup(w => w.UpdateCumulAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SAGE indisponible"));

        await FluentActions.Awaiting(() => e.Sut().SyncExtrasAsync(Periode(), "CANTINE_CODE", true, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        e.Uow.Get<PaieEnvoi>().Items.Single().Statut.Should().Be("ECHEC");
        e.Uow.Get<PaieInstantane>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_SnapshotNotWritten_MarksTheSendIncertain_AndWarns()
    {
        var e = new Envoi();
        e.Cantine("04634", 4);
        // La période est clôturée pendant l'envoi : le statut SUCCES et l'instantané sont refusés sous verrou.
        e.Writer.Setup(w => w.UpdateCumulAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (e.Uow.Get<PaieCloture>().Items.Count == 0)
                    e.Uow.Get<PaieCloture>().Items.Add(new PaieCloture { Id = 1, BddSage = Base, PeriodeDebut = Debut, PeriodeFin = Fin, Empreinte = new string('a', 64) });
                return 1;
            });

        var resultat = await e.Sut().SyncExtrasAsync(Periode(), "CANTINE_CODE", true, CancellationToken.None);

        e.Uow.Get<PaieEnvoi>().Items.Single().Statut.Should().Be("INCERTAIN");
        e.Uow.Get<PaieInstantane>().Items.Should().BeEmpty();
        resultat.Message.Should().EndWith(StandardPayrollWriter.InstantaneNonEcrit);
    }

    [Fact]
    public async Task HoursSendLog_IsUnchanged_AndIgnoresCanteenSendsForTheConfigurationLock()
    {
        var e = new Envoi();
        e.Cantine("04634", 4);
        await e.Sut().SyncExtrasAsync(Periode(), "CANTINE_CODE", true, CancellationToken.None);
        var heures = new[] { new PayrollSnapshotLine("04634", 103, "Exo130", "HS01", "H", 1.5m, false) };

        await e.SendLog.RecordWithSnapshotAsync(new PayrollSend(Base, "AUTRE", Debut.AddDays(7), Fin.AddDays(7), "rh", "SUCCES", 1),
            new PayrollSnapshotDraft(heures, 0, 0, PayrollSendFilter.Aucun, true, 8m));

        var envoiHeures = e.Uow.Get<PaieEnvoi>().Items.Single(x => x.PeriodeDebut == Debut.AddDays(7));
        envoiHeures.Domaine.Should().Be(PayrollDomains.Heures, "le domaine par défaut d'un envoi est HEURES");
        var instantaneHeures = e.Uow.Get<PaieInstantane>().Items.Single(i => i.EnvoiId == envoiHeures.Id);
        instantaneHeures.VersionFormat.Should().Be(1);
        instantaneHeures.Empreinte.Should().Be(PayrollSnapshotFingerprint.Calculer(Base, Debut.AddDays(7), Fin.AddDays(7), "AUTRE", heures));
        (await e.SendLog.ListPeriodsAsync(Base)).Should().Equal((Debut.AddDays(7), Fin.AddDays(7)));
    }

    // =====================================================================================================
    // Clôture : contrôle par domaine
    // =====================================================================================================

    /// <summary>Environnement de clôture (comme le lot D2) : heures envoyées sans filtre, concordantes, Contrôle « Prêt ».</summary>
    private sealed class Ctx
    {
        public InMemoryUnitOfWork Uow { get; } = new();
        public Mock<IClosingService> Controle { get; } = new();
        public PayrollSendLog SendLog { get; }
        public string TypeBase { get; set; } = "STANDARD";

        public Ctx()
        {
            SendLog = new PayrollSendLog(new ScopeFactory(Uow), NullLogger<PayrollSendLog>.Instance);
            Controle.Setup(c => c.AnalyserAsync(It.IsAny<ClosingAnalysisRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ClosingAnalysisDto(Debut, Fin, null, 100, "f", 5, 0, [new ClosingCountDto("Critique", 0)], [],
                    new PagedResult<ClosingAnomalyDto>([], 0, 1, 10, 1), [], null, "Pret"));
            Uow.Get<HsExoImp>().Items.Add(new HsExoImp { Oid = 1, Matricule = "04634", NumSal = 103, Exo130 = 1.5m, PeriodeDebut = Debut, PeriodeFin = Fin, BddSage = Base });
        }

        public ITenantConnectionService Tenant() => PayrollClosureD5Tests.Tenant(TypeBase);

        public PayrollClosureService Sut()
        {
            var rights = new Mock<IEffectiveRightsService>();
            rights.Setup(r => r.GetAsync(It.IsAny<decimal>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new EffectiveRights
                {
                    RoleId = 91, Gates = new HashSet<int> { 1 },
                    Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"SAGE:{Base}:CLOTURE" }
                });
            var horloge = new Mock<TimeProvider>();
            horloge.Setup(h => h.GetUtcNow()).Returns(Aujourdhui);
            horloge.Setup(h => h.LocalTimeZone).Returns(TimeZoneInfo.Utc);
            return new PayrollClosureService(Uow, Tenant(), new Http.TestCurrentUser(42, 91), rights.Object, Controle.Object,
                Microsoft.Extensions.Options.Options.Create(new ClosingOptions { Enabled = true }),
                Microsoft.Extensions.Options.Options.Create(new AuditJournalOptions { Enabled = true }), horloge.Object,
                new PayrollVariableCache(), new ClosingAnalysisCache(), Mock.Of<IClosingChecklistCache>(), new PayrollClosureGuard(Uow, Tenant()),
                Mock.Of<ICompanyContext>(), NullLogger<PayrollClosureService>.Instance);
        }

        public Task HeuresAsync() => SendLog.RecordWithSnapshotAsync(new PayrollSend(Base, "STANDARD", Debut, Fin, "rh", "SUCCES", 1),
            new PayrollSnapshotDraft([new PayrollSnapshotLine("04634", 103, "Exo130", "HS01", "H", 1.5m, false)], 0, 0, PayrollSendFilter.Aucun, true, 8m));

        public void Activer(string domaine) => Uow.Get<PaieOption>().Items.Add(new PaieOption
        {
            Id = Uow.Get<PaieOption>().Items.Count + 1, BddSage = Base, Cle = PayrollOptions.CleDomaine(domaine), Valeur = "1"
        });

        /// <summary>Envoi d'un domaine (marqueur, puis fin SUCCES avec instantané) et totaux actuels concordants.</summary>
        public async Task<int> DomaineAsync(string domaine, decimal valeur = 4m, string statut = "SUCCES", PayrollSendFilter? filtre = null,
            DateTime? debut = null, DateTime? fin = null, bool totaux = true)
        {
            var (d, f) = (debut ?? Debut, fin ?? Fin);
            var id = await SendLog.RecordAsync(new PayrollSend(Base, "STANDARD", d, f, "rh", "EN_COURS", 0, domaine));
            var destination = domaine == PayrollDomains.Cantine ? "CANTINE_CODE" : "HA06";
            if (statut == "SUCCES")
                await SendLog.CompleteWithSnapshotAsync(id, "SUCCES", 1, new PayrollSnapshotDraft(
                    [new PayrollSnapshotLine("04634", 103, domaine, destination, PayrollDomains.UniteNombre, valeur, false)], 0, 0,
                    filtre ?? PayrollSendFilter.Aucun, false, 0m));
            else
                await SendLog.CompleteAsync(id, statut, 1);
            if (totaux && domaine == PayrollDomains.Cantine)
                Uow.Get<Cantine>().Items.Add(new Cantine { IdCantine = id, NomBddSage = Base, MatriculeSage = "04634", DateDeb = d, DateFin = f, TotalCantine = (int)valeur });
            else if (totaux)
                Uow.Get<Frais>().Items.Add(new Frais { IdPointage = id, NomBddSage = Base, MatriculeSage = "04634", DateDeb = d, DateFin = f, TotalPoint = (int)valeur });
            return id;
        }

        public async Task<ConflictException> RefusAsync()
        {
            var ex = (await FluentActions.Awaiting(() => Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin)))
                .Should().ThrowAsync<ConflictException>()).Which;
            Uow.Get<PaieCloture>().Items.Should().BeEmpty("un refus n'écrit rien");
            Uow.Get<PaieClotureDomaine>().Items.Should().BeEmpty();
            return ex;
        }
    }

    private static ITenantConnectionService Tenant(string typeBase = "STANDARD")
    {
        var tenant = new Mock<ITenantConnectionService>();
        tenant.Setup(t => t.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddSage { Id = 3033, NomBd = Base, TypeBase = typeBase });
        tenant.SetupGet(t => t.SageDb).Returns(Base);
        tenant.Setup(t => t.GetSageConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("Server=sage");
        return tenant.Object;
    }

    private const string Bornes = "du 17/08/2026 au 23/08/2026";

    [Fact]
    public async Task Close_NoDomainActivatedNorSent_ClosesTheHoursAsBefore()
    {
        var c = new Ctx();
        await c.HeuresAsync();

        var cloture = await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        cloture.Domaines.Should().BeEmpty();
        c.Uow.Get<PaieClotureDomaine>().Items.Should().BeEmpty();
        cloture.Empreinte.Should().Be(c.Uow.Get<PaieInstantane>().Items.Single().Empreinte);
        var journal = c.Uow.Get<JournalAudit>().Items.Single(j => j.Actions == "CLOTURE_PAIE");
        journal.Nouveaux.Should().Contain("Domaines=HEURES;");
        journal.Tables.Should().Be("T_PAIE_CLOTURE");
    }

    [Fact]
    public async Task Close_ActivatedCanteenNeverSent_IsRefusedNamingTheDomain()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        c.Activer(PayrollDomains.Cantine);

        (await c.RefusAsync()).Message.Should().Be($"Cantine : aucun envoi en paie sur les bornes exactes de la période ({Bornes}). "
            + "Calculez puis envoyez la cantine sur ces bornes, sans plage de matricules, avant de clôturer.");
    }

    [Fact]
    public async Task Close_ActivatedDomain_LastSendIncertainOrEchec_IsRefused()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        c.Activer(PayrollDomains.Frais);
        await c.DomaineAsync(PayrollDomains.Frais, statut: "INCERTAIN");

        (await c.RefusAsync()).Message.Should().StartWith($"Frais de déplacement : le dernier envoi de la période ({Bornes}) est INCERTAIN")
            .And.EndWith("Calculez puis envoyez les frais de déplacement sur ces bornes, sans plage de matricules, avant de clôturer.");

        await c.DomaineAsync(PayrollDomains.Frais, statut: "ECHEC", totaux: false);
        (await c.RefusAsync()).Message.Should().Contain("n'a pas réussi (statut ECHEC)");
    }

    [Fact]
    public async Task Close_ActivatedDomain_LastSendFiltered_IsRefused()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        c.Activer(PayrollDomains.Cantine);
        await c.DomaineAsync(PayrollDomains.Cantine, filtre: PayrollSendFilter.De(null, "04000", null));

        (await c.RefusAsync()).Message.Should().Be($"Cantine : le dernier envoi de la période ({Bornes}) porte une plage de matricules. "
            + "Calculez puis envoyez la cantine sur ces bornes, sans plage de matricules, avant de clôturer.");
    }

    [Fact]
    public async Task Close_DomainNotActivated_ButSentOnOtherBounds_IsStillRequired()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        // Cantine envoyée sur le mois civil (bornes différentes) sans être activée : exigée quand même, sur les bornes exactes.
        await c.DomaineAsync(PayrollDomains.Cantine, debut: new DateTime(2026, 8, 1), fin: new DateTime(2026, 8, 31));

        (await c.RefusAsync()).Message.Should().StartWith($"Cantine : aucun envoi en paie sur les bornes exactes de la période ({Bornes}).");
    }

    [Fact]
    public async Task Close_DomainNotActivated_AndNotSent_IsNotRequired_EvenIfSentOutsideThePeriod()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        await c.DomaineAsync(PayrollDomains.Frais, debut: new DateTime(2026, 9, 1), fin: new DateTime(2026, 9, 30));

        var cloture = await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        cloture.Domaines.Should().BeEmpty();
    }

    [Fact]
    public async Task Close_BothDomainsValid_FreezesEachFingerprint_AndJournalsThem()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        c.Activer(PayrollDomains.Cantine);
        c.Activer(PayrollDomains.Frais);
        var cantine = await c.DomaineAsync(PayrollDomains.Cantine, 4m);
        var frais = await c.DomaineAsync(PayrollDomains.Frais, 6m);

        var cloture = await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        var instantanes = c.Uow.Get<PaieInstantane>().Items;
        var figes = c.Uow.Get<PaieClotureDomaine>().Items.OrderBy(d => d.Domaine).ToList();
        figes.Select(f => (f.Domaine, f.EnvoiId, f.VersionFormat)).Should().Equal((PayrollDomains.Cantine, cantine, 2), (PayrollDomains.Frais, frais, 2));
        figes.Should().OnlyContain(f => f.ClotureId == cloture.Id && f.Empreinte == instantanes.Single(i => i.EnvoiId == f.EnvoiId).Empreinte);
        cloture.Domaines.Select(d => d.Domaine).Should().Equal(PayrollDomains.Cantine, PayrollDomains.Frais);
        cloture.Empreinte.Should().Be(instantanes.Single(i => i.Domaine == PayrollDomains.Heures).Empreinte, "l'empreinte des heures ne change pas");
        var journal = c.Uow.Get<JournalAudit>().Items.Single(j => j.Actions == "CLOTURE_PAIE");
        journal.Tables.Should().Be("T_PAIE_CLOTURE, T_PAIE_CLOTURE_DOMAINE");
        journal.Nouveaux.Should().Contain($"Domaines=HEURES, CANTINE:Envoi={cantine}:Format=2:Empreinte={figes[0].Empreinte}, FRAIS:Envoi={frais}:Format=2:");
    }

    [Fact]
    public async Task Close_CanteenRecomputedAfterTheSend_IsRefused()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        c.Activer(PayrollDomains.Cantine);
        await c.DomaineAsync(PayrollDomains.Cantine, 4m);
        c.Uow.Get<Cantine>().Items.Single().TotalCantine = 5;

        (await c.RefusAsync()).Message.Should().Be($"Cantine : les totaux actuels de la période ({Bornes}) ne correspondent plus à l'envoi figé "
            + "(1 valeur(s) différente(s)) : ils ont été recalculés depuis l'envoi. Envoyez la cantine sur ces bornes, sans plage de matricules, avant de clôturer.");
    }

    [Fact]
    public async Task Close_AlteredDomainSnapshot_IsRefused()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        c.Activer(PayrollDomains.Frais);
        await c.DomaineAsync(PayrollDomains.Frais, 6m);
        c.Uow.Get<PaieInstantaneLigne>().Items.Single(l => l.Variable == PayrollDomains.Frais).Valeur = 7m;
        c.Uow.Get<Frais>().Items.Single().TotalPoint = 7;

        (await c.RefusAsync()).Message.Should().StartWith($"Frais de déplacement : l'instantané du dernier envoi de la période ({Bornes}) est altéré");
    }

    [Fact]
    public async Task Close_OtherBase_ActivationIgnored_DomainsNotRequired()
    {
        var c = new Ctx { TypeBase = "AUTRE" };
        await c.HeuresAsync();
        c.Activer(PayrollDomains.Cantine);
        c.Activer(PayrollDomains.Frais);

        var cloture = await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));

        cloture.Domaines.Should().BeEmpty();
    }

    [Fact]
    public async Task Close_HoursRulesStillComeFirst()
    {
        var c = new Ctx();
        c.Activer(PayrollDomains.Cantine);
        await c.DomaineAsync(PayrollDomains.Cantine);

        (await c.RefusAsync()).Message.Should().Be(PayrollClosureService.AucunEnvoi, "un envoi de cantine n'est pas un envoi d'heures");
    }

    // =====================================================================================================
    // État par domaine (carte de clôture)
    // =====================================================================================================

    [Fact]
    public async Task ListPeriods_ShowsTheStateOfEachDomain_AndCanteenSendsDoNotCreatePeriods()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        c.Activer(PayrollDomains.Cantine);
        await c.DomaineAsync(PayrollDomains.Frais, debut: new DateTime(2026, 9, 1), fin: new DateTime(2026, 9, 30));

        var periodes = await c.Sut().ListPeriodsAsync();

        var periode = periodes.Periodes.Should().ContainSingle("un envoi de frais seul ne crée pas de période").Which;
        periode.Domaines.Select(d => (d.Domaine, d.Etat, d.Exige, d.Actif)).Should().Equal(
            (PayrollDomains.Heures, "ENVOYE", true, true),
            (PayrollDomains.Cantine, "MANQUANT", true, true),
            (PayrollDomains.Frais, "NON_EXIGE", false, false));
        periode.Domaines[1].Motif.Should().StartWith("Cantine : aucun envoi en paie sur les bornes exactes");
        periode.Domaines[0].Empreinte.Should().MatchRegex("^[0-9a-f]{64}$");

        await c.DomaineAsync(PayrollDomains.Cantine);
        await c.Sut().CloseAsync(new PayrollClosureRequest(Debut, Fin));
        var apres = (await c.Sut().ListPeriodsAsync()).Periodes.Single();
        apres.Etat.Should().Be("CLOTUREE");
        apres.Domaines.Select(d => (d.Domaine, d.Etat)).Should().Equal(
            (PayrollDomains.Heures, "FIGE"), (PayrollDomains.Cantine, "FIGE"), (PayrollDomains.Frais, "NON_EXIGE"));
        apres.Cloture!.Domaines.Should().ContainSingle().Which.Domaine.Should().Be(PayrollDomains.Cantine);
    }

    [Fact]
    public async Task SnapshotList_ReturnsEveryDomain_WithItsName()
    {
        var c = new Ctx();
        await c.HeuresAsync();
        await c.DomaineAsync(PayrollDomains.Cantine);

        var envois = await PayrollSnapshotService_EnvoisAsync(c.Uow);

        envois.Select(e => e.Domaine).Should().BeEquivalentTo([PayrollDomains.Heures, PayrollDomains.Cantine]);
        envois.Single(e => e.Domaine == PayrollDomains.Cantine).VersionFormat.Should().Be(2);
    }

    private static Task<IReadOnlyList<PayrollSendSummaryDto>> PayrollSnapshotService_EnvoisAsync(IUnitOfWork uow)
    {
        var autorisation = new Mock<IDatabaseConfigurationAuthorizer>();
        return new PayrollSnapshotService(uow, Tenant(), autorisation.Object).ListAsync(Debut, Fin);
    }

    // =====================================================================================================
    // Activation par base
    // =====================================================================================================

    private static (PayrollVariableService Sut, InMemoryUnitOfWork Uow, RecordingSendLog Envois) Variables(string typeBase = "STANDARD")
    {
        var uow = new InMemoryUnitOfWork();
        var tenant = new Mock<ITenantConnectionService>();
        tenant.SetupGet(t => t.SageDb).Returns(Base);
        tenant.Setup(t => t.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddSage { NomBd = Base, TypeBase = typeBase });
        var constantes = new Mock<ICodeConstanteService>();
        constantes.Setup(c => c.GetOvertimeSageCodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new OvertimeSageCodes());
        constantes.Setup(c => c.ListSageConstantOptionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var envois = new RecordingSendLog();
        var horloge = new Mock<TimeProvider>();
        horloge.Setup(h => h.GetUtcNow()).Returns(Aujourdhui);
        horloge.Setup(h => h.LocalTimeZone).Returns(TimeZoneInfo.Utc);
        var sut = new PayrollVariableService(uow, tenant.Object, Mock.Of<IDatabaseConfigurationAuthorizer>(), new TestCurrentUser(5, 1), envois,
            constantes.Object, Mock.Of<IExternalDiscoveryService>(), new PayrollVariableCache(), new ClosingAnalysisCache(), new ClosingChecklistCache(),
            horloge.Object, NullLogger<PayrollVariableService>.Instance, new FakePayrollTargetSchema());
        return (sut, uow, envois);
    }

    [Fact]
    public async Task Domains_AreDisabledByDefault_AndSavedWithTheirAuthor_EvenWhenTheMonthIsSent()
    {
        var (sut, uow, envois) = Variables();
        envois.Periodes.Add((new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)));

        (await sut.GetConfigurationAsync()).Domaines.Should().Be(new PayrollDomainsDto(false, false));
        var config = await sut.SaveDomainsAsync(new PayrollDomainsDto(true, false));

        config.Domaines.Should().Be(new PayrollDomainsDto(true, false));
        var ligne = uow.Get<PaieOption>().Items.Should().ContainSingle("enregistrer la valeur par défaut ne crée rien").Which;
        (ligne.Cle, ligne.Valeur, ligne.ModifiePar, ligne.BddSage).Should().Be(("DomaineCantineActif", "1", "u5", Base));

        await sut.SaveDomainsAsync(new PayrollDomainsDto(false, true));
        uow.Get<PaieOption>().Items.Select(o => (o.Cle, o.Valeur)).Should().BeEquivalentTo([("DomaineCantineActif", "0"), ("DomaineFraisActif", "1")]);
    }

    [Fact]
    public async Task Domains_CannotBeActivatedForAnOtherBase()
    {
        var (sut, uow, _) = Variables("AUTRE");

        await FluentActions.Awaiting(() => sut.SaveDomainsAsync(new PayrollDomainsDto(false, true)))
            .Should().ThrowAsync<DomainException>().WithMessage("*ne s'envoient qu'à une base SAGE STANDARD*");
        uow.Get<PaieOption>().Items.Should().BeEmpty();
        (await sut.SaveDomainsAsync(new PayrollDomainsDto(false, false))).Domaines.Should().Be(new PayrollDomainsDto(false, false));
    }

    [Fact]
    public void DomainController_HasASinglePutForTheSetting()
    {
        var methode = typeof(Api.Controllers.PayrollVariablesController).GetMethod(nameof(Api.Controllers.PayrollVariablesController.SaveDomainsAsync))!;
        methode.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPutAttribute), false).Cast<Microsoft.AspNetCore.Mvc.HttpPutAttribute>()
            .Single().Template.Should().Be("domains");
    }

    // =====================================================================================================
    // Migration 011
    // =====================================================================================================

    [Fact]
    public void Script011_Domains_IsAdditive_AndReplayable()
    {
        var script = new EmbeddedMigrationScriptSource().Load().Should().ContainSingle(s => s.Version == 11).Which;
        script.Name.Should().Be("011_domaines_paie.sql");
        var code = script.Content[(script.Content.IndexOf("*/", StringComparison.Ordinal) + 2)..];
        foreach (var interdit in new[] { "DROP ", "TRUNCATE", "ALTER COLUMN", "USE [", "CREATE DATABASE", "INSERT ", "UPDATE dbo", "DELETE FROM", "DISABLE TRIGGER", "EXEC sp_rename" })
            code.Should().NotContainEquivalentOf(interdit);
        code.Should().Contain("IF COL_LENGTH(N'dbo.T_PAIE_ENVOI', N'DOMAINE') IS NULL")
            .And.Contain("IF COL_LENGTH(N'dbo.T_PAIE_INSTANTANE', N'DOMAINE') IS NULL")
            .And.Contain("DEFAULT (N'HEURES')")
            .And.Contain("CHECK (DOMAINE IN (N'HEURES', N'CANTINE', N'FRAIS'))")
            .And.Contain("OBJECT_ID(N'dbo.T_PAIE_CLOTURE_DOMAINE', N'U') IS NULL")
            .And.Contain("REFERENCES dbo.T_PAIE_CLOTURE (ID)")
            .And.Contain("OBJECT_ID(N'dbo.TR_T_PAIE_CLOTURE_DOMAINE_IMMUABLE', N'TR') IS NULL")
            .And.Contain("INSTEAD OF UPDATE, DELETE")
            .And.Contain("THROW 51992");
    }

    [Fact]
    public void StartupGuard_NamesScript011()
        => SchemaPresenceGuard.MissingPayrollDomainsMessage.Should().Contain("011_domaines_paie.sql").And.Contain("T_PAIE_CLOTURE_DOMAINE");

    private sealed class ScopeFactory(IUnitOfWork uow) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(uow);

        private sealed class Scope(IUnitOfWork uow) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => serviceType == typeof(IUnitOfWork) ? uow : null;
            public void Dispose() { }
        }
    }
}
