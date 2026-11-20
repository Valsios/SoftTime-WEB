using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Integrations;

namespace SoftTime.Tests;

/// <summary>
/// Lot D1 : l'instantané contient exactement les valeurs écrites par les writers STANDARD et AUTRE (zéros de remise à 0
/// compris, variables décochées et mises à jour sans ligne exclues) ; un échec de l'instantané rend l'envoi INCERTAIN
/// (STANDARD) ou annule la paie (AUTRE).
/// </summary>
public partial class PayrollVariableWritersTests
{
    private static readonly HsExoDto ResetRow = new(0, "MAT03", 103, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m) { SansValeur = true };

    [Fact]
    public async Task StandardSnapshot_ContainsExactlyTheWrittenValues_WithResetZerosFlagged()
    {
        // Arrange : la constante HS07 (Nuit) n'a aucune ligne T_CUMSAL : comptée, pas figée.
        var envois = new RecordingSendLog();
        var sut = new TestStandardWriter(new CapturingSagePayrollWriter(lignes: op => op == 7 ? 0 : 1), new NullAudit(), sendLog: envois);

        // Act
        await sut.WriteAsync(Base(), [Ligne, ResetRow], Contexte());

        // Assert
        var instantane = envois.Instantanes.Should().ContainSingle().Which.Value;
        instantane.SansLigne.Should().Be(2);
        instantane.Lignes.Should().HaveCount(12).And.NotContain(l => l.Variable == "Nuit");
        instantane.Lignes.Where(l => l.Matricule == "MAT01").Select(l => (l.Variable, l.Destination, l.Unite, l.Valeur, l.NumSal, l.RemiseAZero))
            .Should().Equal(
                ("Exo130", "HS01", "H", 1.5m, (int?)101, false), ("Exo150", "HS02", "H", 2m, (int?)101, false),
                ("I130", "HS03", "H", 0.5m, (int?)101, false), ("I150", "HS04", "H", 1.25m, (int?)101, false),
                ("Ferie", "HS05", "H", 3m, (int?)101, false), ("Dimanche", "HS06", "H", 2.5m, (int?)101, false));
        instantane.Lignes.Where(l => l.Matricule == "MAT03").Should().HaveCount(6).And.OnlyContain(l => l.RemiseAZero && l.Valeur == 0m);
        envois.Envois.Single().Status.Should().Be("SUCCES");
    }

    [Fact]
    public async Task StandardSnapshot_UncheckedVariable_IsAbsent_AndTheFilterIsKept()
    {
        var envois = new RecordingSendLog();
        var sut = new TestStandardWriter(new CapturingSagePayrollWriter(), new NullAudit(), new FixedVariableService(Sans("Ferie")), envois);

        await sut.WriteAsync(Base(), [Ligne], Contexte() with { Filtre = PayrollSendFilter.De(" BR704 ", null, null) });

        var instantane = envois.Instantanes.Single().Value;
        instantane.Lignes.Select(l => l.Variable).Should().Equal("Exo130", "Exo150", "I130", "I150", "Dimanche", "Nuit");
        instantane.Filtre.Should().Be(new PayrollSendFilter("BR704", null, null));
        instantane.Filtre.AvecFiltre.Should().BeTrue();
    }

    [Fact]
    public async Task StandardSnapshot_WriteFails_SendIsMarkedUncertain_AndThePayrollStaysWritten()
    {
        // Arrange
        var sage = new CapturingSagePayrollWriter();
        var envois = new RecordingSendLog { EchecInstantane = true };

        // Act
        var resultat = await new TestStandardWriter(sage, new NullAudit(), sendLog: envois).WriteAsync(Base(), [Ligne], Contexte());

        // Assert : la paie SAGE est écrite (non transactionnelle), l'envoi est INCERTAIN, l'utilisateur est prévenu.
        sage.Calls.Should().HaveCount(7);
        envois.Envois.Single().Status.Should().Be("INCERTAIN");
        envois.Instantanes.Should().BeEmpty();
        resultat.Message.Should().EndWith(StandardPayrollWriter.InstantaneNonEcrit);
    }

    [Fact]
    public async Task StandardSnapshot_FailedSend_HasNoSnapshot()
    {
        var envois = new RecordingSendLog();
        var sut = new TestStandardWriter(new CapturingSagePayrollWriter(echecApres: 3), new NullAudit(), sendLog: envois);

        await FluentActions.Awaiting(() => sut.WriteAsync(Base(), [Ligne], Contexte())).Should().ThrowAsync<InvalidOperationException>();

        envois.Envois.Single().Status.Should().Be("ECHEC");
        envois.Instantanes.Should().BeEmpty();
    }

    [Fact]
    public async Task StandardSnapshot_EmployeeWithoutNumber_IsCountedNotReset()
    {
        var envois = new RecordingSendLog();
        var sut = new TestStandardWriter(new CapturingSagePayrollWriter(), new NullAudit(), sendLog: envois);

        await sut.WriteAsync(Base(), [Ligne, ResetRow with { NumSal = null }], Contexte());

        var instantane = envois.Instantanes.Single().Value;
        instantane.NonRemis.Should().Be(1);
        instantane.Lignes.Should().OnlyContain(l => l.Matricule == "MAT01");
    }

    [Fact]
    public async Task OtherSnapshot_ContainsTheWrittenColumns_AndIsRecordedWithTheMarkerBeforeTheCommit()
    {
        // Arrange : MAT03 (sans valeur) absent de la table de paie : ignoré, compté comme non remis.
        var (sut, db, journal, envois) = Autre(selection: null);
        db.LignesParMatricule = m => m == "MAT03" ? 0L : 1L;

        // Act
        await sut.WriteAsync(BaseAutre(), [Ligne, ResetRow], Contexte());

        // Assert
        var instantane = envois.Instantanes.Should().ContainSingle().Which.Value;
        instantane.NonRemis.Should().Be(1);
        instantane.Lignes.Select(l => (l.Matricule, l.Variable, l.Destination, l.Valeur)).Should().Equal(
            ("MAT01", "Exo130", "dbo.PAIE_CUMULS.HeuresExo130", 1.5m), ("MAT01", "Exo150", "dbo.PAIE_CUMULS.HeuresExo150", 2m),
            ("MAT01", "I130", "dbo.PAIE_CUMULS.HeuresI130", 0.5m), ("MAT01", "I150", "dbo.PAIE_CUMULS.HeuresI150", 1.25m),
            ("MAT01", "Ferie", "dbo.PAIE_CUMULS.HeuresFerie", 3m), ("MAT01", "Dimanche", "dbo.PAIE_CUMULS.HeuresDimanche", 2.5m),
            ("MAT01", "Nuit", "dbo.PAIE_CUMULS.HeuresNuit", 4m));
        instantane.Lignes.Should().OnlyContain(l => l.NumSal == null && l.Unite == "H");
        journal.IndexOf("marqueur").Should().BeLessThan(journal.IndexOf("commit"));
    }

    [Fact]
    public async Task OtherSnapshot_WriteFails_ThePayrollIsRolledBack()
    {
        var (sut, _, journal, envois) = Autre(selection: null);
        envois.EchecInstantane = true;

        await FluentActions.Awaiting(() => sut.WriteAsync(BaseAutre(), [Ligne], Contexte())).Should().ThrowAsync<InvalidOperationException>();

        journal.Should().Contain("rollback").And.NotContain("commit");
        envois.Envois.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherSnapshot_CommitFails_SendIsUncertain()
    {
        var (sut, db, _, envois) = Autre(selection: null);
        db.EchecCommit = true;

        await FluentActions.Awaiting(() => sut.WriteAsync(BaseAutre(), [Ligne], Contexte())).Should().ThrowAsync<InvalidOperationException>();

        envois.Envois.Single().Status.Should().Be("INCERTAIN");
        envois.Instantanes.Should().ContainSingle("l'instantané reste, mais un envoi INCERTAIN ne permet pas de clôturer");
    }

    [Fact]
    public async Task StandardSnapshot_AbsenceInDays_IsFrozenInTheChosenUnit()
    {
        var selection = PayrollVariableSelection.Avec(["Exo130", "Absence"]) with
        {
            Unites = new Dictionary<string, string>(StringComparer.Ordinal) { ["Absence"] = "J" },
            CodesPaie = new Dictionary<string, string>(StringComparer.Ordinal) { ["Absence"] = "ABS01" },
            HeuresParJour = 8m
        };
        var envois = new RecordingSendLog();
        var sut = new AbsenceStandardWriter(new CapturingSagePayrollWriter(), new FixedVariableService(selection), envois);

        await sut.WriteAsync(Base(), [Ligne with { Absence = 24m }], Contexte());

        var instantane = envois.Instantanes.Single().Value;
        instantane.Lignes.Select(l => (l.Variable, l.Destination, l.Unite, l.Valeur)).Should().Equal(
            ("Exo130", "HS01", "H", 1.5m), ("Absence", "ABS01", "J", 3m));
        instantane.HeuresParJour.Should().Be(8m);
    }

    private sealed class AbsenceStandardWriter(ISagePayrollWriter sageWriter, IPayrollVariableService variables, IPayrollSendLog sendLog)
        : StandardPayrollWriter(sageWriter, null!, null!, null!, new NullAudit(), variables, sendLog)
    {
        protected override Task<SagePayrollTarget> GetInputAsync(CancellationToken ct) => Task.FromResult(Cible() with
        {
            Constants = Cible().Constants.Append(new SoftTime.Domain.Entities.Sage.ConstanteSage { CodeConstante = "ABS01", CodeOperande1 = 9 }).ToArray()
        });
    }
}
