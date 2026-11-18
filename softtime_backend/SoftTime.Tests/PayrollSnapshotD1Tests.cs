using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using SoftTime.Application.Abstractions.DataSources;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Domain.Repositories;
using SoftTime.Infrastructure.SchemaMigrations;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

/// <summary>
/// Lot D1 : instantané des envois en paie (T_PAIE_INSTANTANE, T_PAIE_INSTANTANE_LIGNE), empreinte SHA-256 canonique et
/// versionnée, écriture avec le marqueur d'envoi dans une transaction SoftTime, lecture et vérification en lecture seule.
/// </summary>
public class PayrollSnapshotD1Tests
{
    private static readonly DateTime Debut = new(2026, 9, 14);
    private static readonly DateTime Fin = new(2026, 10, 11);

    private static readonly PayrollSnapshotLine[] Lignes =
    [
        new("04634", 103, "Exo130", "HS01", "H", 1.5m, false),
        new("04634", 103, "Retard", "RET01", "H", 0.25m, false),
        new("04700", 104, "Exo130", "HS01", "H", 0m, true),
    ];

    // ---------- Empreinte ----------

    [Fact]
    public void Fingerprint_CanonicalForm_IsTheDocumentedOne()
    {
        var canonique = PayrollSnapshotFingerprint.Canonique(1, "SFOI_PAIE2025", Debut, Fin, "standard", Lignes.Reverse());

        canonique.Should().Be(
            "SOFTTIME-PAIE-INSTANTANE|1|SFOI_PAIE2025|2026-09-14|2026-10-11|STANDARD\n"
            + "04634|103|Exo130|HS01|H|1.5000|0\n"
            + "04634|103|Retard|RET01|H|0.2500|0\n"
            + "04700|104|Exo130|HS01|H|0.0000|1");
        PayrollSnapshotFingerprint.Calculer("SFOI_PAIE2025", Debut, Fin, "STANDARD", Lignes)
            .Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Fingerprint_IsDeterministic_WhateverTheOrderAndTheCulture()
    {
        var reference = PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD", Lignes);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD", Lignes.Reverse()).Should().Be(reference);
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD", [Lignes[2], Lignes[0], Lignes[1]]).Should().Be(reference);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
        // Espaces de bord et décimales au-delà de 4 : normalisés avant le calcul.
        PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD",
                [Lignes[0] with { Matricule = " 04634 ", Valeur = 1.50000m }, Lignes[1], Lignes[2]])
            .Should().Be(reference);
    }

    public static TheoryData<string, PayrollSnapshotLine> Alterations => new()
    {
        { "valeur", Lignes[0] with { Valeur = 1.5001m } },
        { "matricule", Lignes[0] with { Matricule = "04635" } },
        { "numéro de salarié", Lignes[0] with { NumSal = 999 } },
        { "variable", Lignes[0] with { Variable = "Exo150" } },
        { "destination", Lignes[0] with { Destination = "HS02" } },
        { "unité", Lignes[0] with { Unite = "J" } },
        { "indicateur de remise à 0", Lignes[0] with { RemiseAZero = true } },
    };

    [Theory]
    [MemberData(nameof(Alterations))]
    public void Fingerprint_ChangesWhenAnyFieldChanges(string champ, PayrollSnapshotLine alteree)
    {
        var reference = PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD", Lignes);

        PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD", [alteree, Lignes[1], Lignes[2]])
            .Should().NotBe(reference, champ);
    }

    [Fact]
    public void Fingerprint_ChangesWithTheHeader_AndALineMoreOrLess()
    {
        var reference = PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD", Lignes);

        PayrollSnapshotFingerprint.Calculer("C", Debut, Fin, "STANDARD", Lignes).Should().NotBe(reference);
        PayrollSnapshotFingerprint.Calculer("B", Debut.AddDays(1), Fin, "STANDARD", Lignes).Should().NotBe(reference);
        PayrollSnapshotFingerprint.Calculer("B", Debut, Fin.AddDays(1), "STANDARD", Lignes).Should().NotBe(reference);
        PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "AUTRE", Lignes).Should().NotBe(reference);
        PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD", Lignes.Take(2)).Should().NotBe(reference);
        PayrollSnapshotFingerprint.Calculer("B", Debut, Fin, "STANDARD", Lignes.Append(Lignes[0])).Should().NotBe(reference);
    }

    [Fact]
    public void Fingerprint_UnknownVersion_IsRefused()
    {
        var act = () => PayrollSnapshotFingerprint.Calculer(2, "B", Debut, Fin, "STANDARD", Lignes);

        act.Should().Throw<DomainException>().WithMessage("Format d'empreinte d'instantané inconnu : 2.");
    }

    // ---------- Marqueur et instantané (PayrollSendLog réel, unité de travail en mémoire) ----------

    [Fact]
    public async Task RecordWithSnapshot_WritesTheSendTheHeaderAndTheLines()
    {
        var uow = new InMemoryUnitOfWork();
        var sut = SendLog(uow);

        var id = await sut.RecordWithSnapshotAsync(Envoi("AUTRE", "SUCCES"), Brouillon(PayrollSendFilter.De("BR704", null, null)));

        var envoi = uow.Get<PaieEnvoi>().Items.Single();
        envoi.Id.Should().Be(id);
        envoi.Statut.Should().Be("SUCCES");
        var entete = uow.Get<PaieInstantane>().Items.Single();
        entete.EnvoiId.Should().Be(id);
        entete.BddSage.Should().Be("SFOI_PAIE2025");
        entete.TypeBase.Should().Be("AUTRE");
        entete.PeriodeDebut.Should().Be(Debut);
        entete.PeriodeFin.Should().Be(Fin);
        entete.EnvoyePar.Should().Be("rh");
        entete.Departement.Should().Be("BR704");
        entete.AvecFiltre.Should().BeTrue();
        entete.RemiseAZero.Should().BeTrue();
        entete.HeuresParJour.Should().Be(8m);
        entete.VersionFormat.Should().Be(1);
        entete.NbLignes.Should().Be(3);
        entete.NbSansLigne.Should().Be(5);
        entete.NbNonRemis.Should().Be(2);
        entete.Empreinte.Should().Be(PayrollSnapshotFingerprint.Calculer("SFOI_PAIE2025", Debut, Fin, "AUTRE", Lignes));
        uow.Get<PaieInstantaneLigne>().Items.Should().HaveCount(3).And.OnlyContain(l => l.InstantaneId == entete.Id);
        uow.Get<PaieInstantaneLigne>().Items.Should().ContainSingle(l => l.Matricule == "04700" && l.RemiseAZero && l.Valeur == 0m);
    }

    [Fact]
    public async Task CompleteWithSnapshot_SetsTheFinalStatusAndWritesTheSnapshot_WithoutFilter()
    {
        var uow = new InMemoryUnitOfWork();
        var sut = SendLog(uow);
        var id = await sut.RecordAsync(Envoi("STANDARD", "EN_COURS"));

        await sut.CompleteWithSnapshotAsync(id, "SUCCES", 12, Brouillon(PayrollSendFilter.Aucun));

        uow.Get<PaieEnvoi>().Items.Single().Should().Match<PaieEnvoi>(e => e.Statut == "SUCCES" && e.Lignes == 12);
        var entete = uow.Get<PaieInstantane>().Items.Single();
        entete.AvecFiltre.Should().BeFalse();
        entete.Departement.Should().BeNull();
        entete.TypeBase.Should().Be("STANDARD");
    }

    [Fact]
    public async Task SecondSendOfTheSamePeriod_CreatesASecondSnapshot_AndReplacesNothing()
    {
        var uow = new InMemoryUnitOfWork();
        var sut = SendLog(uow);

        var premier = await sut.RecordWithSnapshotAsync(Envoi("AUTRE", "SUCCES"), Brouillon(PayrollSendFilter.Aucun));
        var second = await sut.RecordWithSnapshotAsync(Envoi("AUTRE", "SUCCES"), Brouillon(PayrollSendFilter.Aucun, Lignes.Take(1).ToArray()));

        second.Should().NotBe(premier);
        uow.Get<PaieInstantane>().Items.Select(i => i.EnvoiId).Should().Equal(premier, second);
        uow.Get<PaieInstantane>().Items.Select(i => i.NbLignes).Should().Equal(3, 1);
        uow.Get<PaieInstantaneLigne>().Items.Should().HaveCount(4);
    }

    [Fact]
    public async Task SendLog_HasNoSnapshotModificationPath()
    {
        // Le contrat du marqueur ne propose aucune méthode qui modifie ou supprime un instantané.
        typeof(IPayrollSendLog).GetMethods().Select(m => m.Name)
            .Should().NotContain(n => n.Contains("Snapshot", StringComparison.Ordinal)
                                      && (n.Contains("Update", StringComparison.Ordinal) || n.Contains("Remove", StringComparison.Ordinal) || n.Contains("Delete", StringComparison.Ordinal)));
        typeof(IPayrollSnapshotService).GetMethods().Select(m => m.Name)
            .Should().BeEquivalentTo("ListAsync", "GetAsync", "VerifyAsync");
        await Task.CompletedTask;
    }

    // ---------- Lecture et vérification ----------

    [Fact]
    public async Task Verify_IntactSnapshot_IsReportedIntact()
    {
        var (uow, svc, id) = await AvecUnEnvoiAsync();

        var verification = await svc.VerifyAsync(id);

        verification.Integre.Should().BeTrue();
        verification.Message.Should().Be(PayrollSnapshotService.Integre);
        verification.EmpreinteRecalculee.Should().Be(verification.EmpreinteStockee);
        verification.NbLignesLues.Should().Be(3);
        uow.Get<PaieInstantane>().Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("valeur")]
    [InlineData("ligne supprimée")]
    [InlineData("ligne ajoutée")]
    [InlineData("empreinte")]
    [InlineData("version")]
    public async Task Verify_AlteredSnapshot_IsReportedAltered(string alteration)
    {
        var (uow, svc, id) = await AvecUnEnvoiAsync();
        var lignes = uow.Get<PaieInstantaneLigne>().Items;
        var entete = uow.Get<PaieInstantane>().Items.Single();
        switch (alteration)
        {
            case "valeur": lignes[0].Valeur += 0.01m; break;
            case "ligne supprimée": lignes.RemoveAt(1); break;
            case "ligne ajoutée": lignes.Add(new PaieInstantaneLigne { Id = 99, InstantaneId = entete.Id, Matricule = "X", Variable = "Nuit", Destination = "HS07", Unite = "H", Valeur = 1m }); break;
            case "empreinte": entete.Empreinte = new string('0', 64); break;
            case "version": entete.VersionFormat = 7; break;
        }

        var verification = await svc.VerifyAsync(id);

        verification.Integre.Should().BeFalse();
        verification.Message.Should().Be(PayrollSnapshotService.Altere);
    }

    [Fact]
    public async Task ListAndDetail_ReadTheSendsOfTheActiveBaseAndPeriod()
    {
        var (uow, svc, id) = await AvecUnEnvoiAsync();
        uow.Get<PaieEnvoi>().Items.Add(new PaieEnvoi { Id = 50, BddSage = "SFOI_PAIE2025", PeriodeDebut = Debut, PeriodeFin = Fin, Statut = "ECHEC", TypeBase = "STANDARD", EnvoyeLe = DateTime.UtcNow.AddDays(-1) });
        uow.Get<PaieEnvoi>().Items.Add(new PaieEnvoi { Id = 51, BddSage = "AUTRE_BASE", PeriodeDebut = Debut, PeriodeFin = Fin, Statut = "SUCCES", TypeBase = "STANDARD", EnvoyeLe = DateTime.UtcNow });

        var liste = await svc.ListAsync(Debut, Fin);
        var detail = await svc.GetAsync(id);

        liste.Select(e => e.EnvoiId).Should().Equal(id, 50);
        liste[0].Empreinte.Should().MatchRegex("^[0-9a-f]{64}$");
        liste[1].InstantaneId.Should().BeNull("un envoi en échec n'a pas d'instantané");
        detail.Lignes.Select(l => (l.Matricule, l.Variable)).Should().Equal(("04634", "Exo130"), ("04634", "Retard"), ("04700", "Exo130"));
        await FluentActions.Awaiting(() => svc.GetAsync(51)).Should().ThrowAsync<NotFoundException>("un envoi d'une autre base n'est pas lisible");
        await FluentActions.Awaiting(() => svc.GetAsync(50)).Should().ThrowAsync<NotFoundException>().WithMessage(PayrollSnapshotService.SansInstantane);
    }

    [Fact]
    public async Task Reading_RequiresTheSameRightAsTheVariables()
    {
        var uow = new InMemoryUnitOfWork();
        var auth = new Mock<IDatabaseConfigurationAuthorizer>();
        auth.Setup(a => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, "SFOI_PAIE2025", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("refus"));
        var svc = new PayrollSnapshotService(uow, Tenant(), auth.Object);

        await FluentActions.Awaiting(() => svc.ListAsync(Debut, Fin)).Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => svc.VerifyAsync(1)).Should().ThrowAsync<ForbiddenException>();
        auth.Verify(a => a.EnsureCanConfigureAsync(DatabaseKind.Sage, DatabaseConfigAction.Update, "SFOI_PAIE2025", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ---------- Migration 009 et démarrage ----------

    [Fact]
    public void Script009_Snapshots_CreatesTablesAndTriggersOnlyByAdditions_AndIsReplayable()
    {
        var scripts = new EmbeddedMigrationScriptSource().Load();

        var script = scripts.Should().ContainSingle(s => s.Version == 9).Which;
        script.Name.Should().Be("009_instantanes_paie.sql");
        var code = script.Content[(script.Content.IndexOf("*/", StringComparison.Ordinal) + 2)..];
        foreach (var interdit in new[] { "DROP ", "TRUNCATE", "ALTER COLUMN", "ALTER TABLE", "USE [", "CREATE DATABASE", "INSERT ", "DISABLE TRIGGER" })
            code.Should().NotContainEquivalentOf(interdit);
        foreach (var garde in new[]
                 {
                     "OBJECT_ID(N'dbo.T_PAIE_INSTANTANE', N'U') IS NULL", "OBJECT_ID(N'dbo.T_PAIE_INSTANTANE_LIGNE', N'U') IS NULL",
                     "name = N'UX_T_PAIE_INSTANTANE_ENVOI'", "name = N'IX_T_PAIE_INSTANTANE_BDD_PERIODE'", "name = N'IX_T_PAIE_INSTANTANE_LIGNE_INSTANTANE'",
                     "OBJECT_ID(N'dbo.TR_T_PAIE_INSTANTANE_IMMUABLE', N'TR') IS NULL", "OBJECT_ID(N'dbo.TR_T_PAIE_INSTANTANE_LIGNE_IMMUABLE', N'TR') IS NULL",
                 })
            code.Should().Contain(garde);
        code.Should().Contain("INSTEAD OF UPDATE, DELETE").And.Contain("REFERENCES dbo.T_PAIE_ENVOI (ID)").And.Contain("THROW 51991");
        foreach (var autre in new[] { "T_CUMSAL", "T_PAIE_ECRITURE", "T_HSExoImp", "suivi_JOURNAL", "T_PAIE_VARIABLE", "T_PAIE_OPTION" })
            code.Should().NotContainEquivalentOf(autre);
        code.Should().NotContainEquivalentOf("Password");
    }

    // ---------- Aides ----------

    private static PayrollSend Envoi(string typeBase, string statut) => new("SFOI_PAIE2025", typeBase, Debut, Fin, "rh", statut, 3);

    private static PayrollSnapshotDraft Brouillon(PayrollSendFilter filtre, PayrollSnapshotLine[]? lignes = null) =>
        new(lignes ?? Lignes, 5, 2, filtre, true, 8m);

    private static PayrollSendLog SendLog(IUnitOfWork uow) => new(new ScopeFactory(uow), NullLogger<PayrollSendLog>.Instance);

    private static ITenantConnectionService Tenant()
    {
        var tenant = new Mock<ITenantConnectionService>();
        tenant.Setup(t => t.GetSageRowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BddSage { Id = 3033, NomBd = "SFOI_PAIE2025", TypeBase = "STANDARD" });
        tenant.SetupGet(t => t.SageDb).Returns("SFOI_PAIE2025");
        return tenant.Object;
    }

    private static async Task<(InMemoryUnitOfWork Uow, PayrollSnapshotService Svc, int Id)> AvecUnEnvoiAsync()
    {
        var uow = new InMemoryUnitOfWork();
        var id = await SendLog(uow).RecordWithSnapshotAsync(Envoi("STANDARD", "SUCCES"), Brouillon(PayrollSendFilter.Aucun));
        return (uow, new PayrollSnapshotService(uow, Tenant(), Mock.Of<IDatabaseConfigurationAuthorizer>()), id);
    }

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
