using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using SoftTime.Application.Abstractions.Audit;
using SoftTime.Application.Abstractions.Payroll;
using SoftTime.Application.Abstractions.RequestContext;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Models;
using SoftTime.Infrastructure.Integrations;
using SoftTime.Infrastructure.Services.Payroll;

namespace SoftTime.Tests;

/// <summary>
/// Envoi vers la paie filtré par les variables cochées (lot A) : caractérisation du comportement par défaut (mêmes
/// commandes, mêmes valeurs, même ordre que dev-refonte), filtre, signalement des mises à jour sans ligne en STANDARD,
/// et marqueur T_PAIE_ENVOI (atomicité AUTRE, échec partiel STANDARD, rien si rien n'est écrit).
/// </summary>
public partial class PayrollVariableWritersTests
{
    private static readonly HsExoDto Ligne = new(1, "MAT01", 101, 1.5m, 2m, 0.5m, 1.25m, 3m, 4m, 2.5m, 7m, 1m);
    private static readonly HsExoDto Ligne2 = new(2, "MAT02", 102, 1m, 1m, 1m, 1m, 1m, 1m, 1m, 0m, 0m);

    // ---------- STANDARD : caractérisation ----------

    [Fact]
    public void UpdateCumulSql_IsTheHistoricalCommand()
        => SagePayrollWriter.UpdateCumulSql.Should().Be(
            "UPDATE T_CUMSAL SET ElementCumul = dbo.SetValeurCstBinaire(0,0,@val) WHERE NumSalarie=@num AND OpCstCumul=@op");

    [Fact]
    public async Task SyncOvertime_DefaultSelection_WritesTheSevenCumulsInHistoricalOrder()
    {
        // Arrange
        var sage = new CapturingSagePayrollWriter();

        // Act
        var updates = await sage.SyncOvertimeAsync(Cible(), 101, OvertimeAmounts.From(Ligne));

        // Assert : opérandes 1..7 = HS01..HS07 ; valeurs EXO130, EXO150, I130, I150, Férié, Dimanche, Nuit.
        sage.Calls.Should().Equal(
            (101, (short)1, 1.5m), (101, (short)2, 2m), (101, (short)3, 0.5m), (101, (short)4, 1.25m),
            (101, (short)5, 3m), (101, (short)6, 2.5m), (101, (short)7, 4m));
        updates.Keys.Should().Equal("Exo130", "Exo150", "I130", "I150", "Ferie", "Dimanche", "Nuit");
    }

    [Fact]
    public async Task StandardWrite_NoConfiguration_IsIdenticalToTheWriterWithoutVariableService()
    {
        // Arrange : service réel, base sans aucune ligne T_PAIE_VARIABLE.
        var historique = new CapturingSagePayrollWriter();
        var configure = new CapturingSagePayrollWriter();
        var service = VariableService(new InMemoryUnitOfWork());

        // Act
        var avant = await new TestStandardWriter(historique, new NullAudit()).WriteAsync(Base(), [Ligne, Ligne2], Contexte());
        var apres = await new TestStandardWriter(configure, new NullAudit(), service).WriteAsync(Base(), [Ligne, Ligne2], Contexte());

        // Assert
        configure.Calls.Should().Equal(historique.Calls);
        apres.Should().Be(avant);
        apres.Message.Should().Be("Synchronisation des heures supplémentaires vers SAGE terminée.");
    }

    // ---------- STANDARD : filtre ----------

    [Fact]
    public async Task SyncOvertime_UncheckedVariable_IsNeitherResolvedNorWritten()
    {
        // Arrange : Nuit décochée ET constante HS07 absente de SAGE.
        var sage = new CapturingSagePayrollWriter();
        var cible = Cible(sansCode: "HS07") with { Variables = Sans(PayrollVariableSelection.Nuit) };

        // Act
        var updates = await sage.SyncOvertimeAsync(cible, 101, OvertimeAmounts.From(Ligne));

        // Assert : pas d'erreur « Constante SAGE HS07 introuvable », aucune écriture de l'opérande 7.
        sage.Calls.Select(c => c.Operand).Should().Equal((short)1, 2, 3, 4, 5, 6);
        updates.Keys.Should().NotContain("Nuit");
    }

    [Fact]
    public async Task SyncOvertime_CheckedVariableWithMissingConstant_StillFailsBeforeAnyWrite()
    {
        // Arrange
        var sage = new CapturingSagePayrollWriter();

        // Act
        var act = () => sage.SyncOvertimeAsync(Cible(sansCode: "HS07"), 101, OvertimeAmounts.From(Ligne));

        // Assert
        (await act.Should().ThrowAsync<PayrollSyncWriteException>()).Which.InnerException!.Message.Should().Contain("HS07");
        sage.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task StandardWrite_RetardActiveWithCode_IsWrittenAfterTheSeven_AbsenceUncheckedNever()
    {
        // Arrange : adapté au lot C2 (avant : Retard et Absence jamais écrites) puis au lot C3 (Absence envoyable : elle est
        // ici décochée, avec un code, et n'est donc pas écrite). Retard a son code RET01 (opérande 8).
        var sage = new CapturingSagePayrollWriter();
        var selection = PayrollVariableSelection.Avec([.. PayrollVariableSelection.HeuresSup, PayrollVariableSelection.Retard])
            with { CodesPaie = new Dictionary<string, string> { [PayrollVariableSelection.Retard] = "RET01", [PayrollVariableSelection.Absence] = "HS01" } };

        // Act
        var resultat = await new TestStandardWriter(sage, new NullAudit(), new FixedVariableService(selection)).WriteAsync(Base(), [Ligne], Contexte());

        // Assert : les sept dans l'ordre historique, puis Retard avec la valeur T_HSExoImp.RETARD (7) ; Absence jamais.
        sage.Calls.Should().Equal(
            (101, (short)1, 1.5m), (101, (short)2, 2m), (101, (short)3, 0.5m), (101, (short)4, 1.25m),
            (101, (short)5, 3m), (101, (short)6, 2.5m), (101, (short)7, 4m), (101, (short)8, 7m));
        resultat.CategoriesUpdated.Should().Be(8);
    }

    [Fact]
    public async Task StandardWrite_RetardActiveWithoutCode_IsRefusedBeforeAnyWrite()
    {
        // Arrange : sélection incohérente (Retard coché sans code) : refus clair, rien d'écrit.
        var sage = new CapturingSagePayrollWriter();
        var selection = PayrollVariableSelection.Avec([.. PayrollVariableSelection.HeuresSup, PayrollVariableSelection.Retard]);

        // Act
        var act = () => new TestStandardWriter(sage, new NullAudit(), new FixedVariableService(selection)).WriteAsync(Base(), [Ligne], Contexte());

        // Assert
        await act.Should().ThrowAsync<Exception>().Where(e => e.ToString().Contains("Aucun code de paie n'est choisi pour la variable Retard"));
        sage.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task StandardWrite_RowsGivenToTheWriter_AreTheOnlyEmployeesTouched()
    {
        // Adapté à C2b : le rédacteur n'écrit que les lignes reçues. La remise à 0 d'un salarié sans ligne de valeurs
        // passe par une ligne synthétique ajoutée par le service de synchronisation (voir PayrollResetPerimeterTests).
        var sage = new CapturingSagePayrollWriter();
        var selection = PayrollVariableSelection.Avec([PayrollVariableSelection.Retard])
            with { CodesPaie = new Dictionary<string, string> { [PayrollVariableSelection.Retard] = "RET01" } };

        await new TestStandardWriter(sage, new NullAudit(), new FixedVariableService(selection)).WriteAsync(Base(), [Ligne], Contexte());

        sage.Calls.Select(c => c.NumSal).Should().OnlyContain(n => n == 101, "le salarié 102, sans ligne, n'est pas touché");
    }

    [Fact]
    public async Task StandardWrite_RetardValueIsTheDisplayedValue_ZeroWhenAbsent()
    {
        // Arrange : la valeur écrite est celle de la ligne T_HSExoImp affichée (2,75 h) ; une valeur absente vaut 0.
        var sage = new CapturingSagePayrollWriter();
        var selection = PayrollVariableSelection.Avec([PayrollVariableSelection.Retard])
            with { CodesPaie = new Dictionary<string, string> { [PayrollVariableSelection.Retard] = "RET01" } };
        var affichee = Ligne with { Retard = 2.75m };
        var sansValeur = Ligne2 with { Retard = null };

        // Act
        await new TestStandardWriter(sage, new NullAudit(), new FixedVariableService(selection)).WriteAsync(Base(), [affichee, sansValeur], Contexte());

        // Assert
        sage.Calls.Should().Equal((101, (short)8, 2.75m), (102, (short)8, 0m));
    }

    // ---------- STANDARD : mises à jour sans ligne ----------

    [Fact]
    public async Task StandardWrite_UpdateWithoutRow_IsReportedWithoutChangingCounts()
    {
        // Arrange : le cumul de l'opérande 5 (Férié) n'existe pas dans SAGE.
        var sage = new CapturingSagePayrollWriter(lignes: op => op == 5 ? 0 : 1);

        // Act
        var resultat = await new TestStandardWriter(sage, new NullAudit()).WriteAsync(Base(), [Ligne], Contexte());

        // Assert
        resultat.UpdatesWithoutRow.Should().Be(1);
        resultat.CategoriesUpdated.Should().Be(6);
        resultat.Message.Should().Contain("1 mise(s) à jour n'ont modifié aucune ligne T_CUMSAL");
    }

    [Fact]
    public async Task StandardWrite_AllRowsUpdated_KeepsTheHistoricalMessage()
    {
        // Act
        var resultat = await new TestStandardWriter(new CapturingSagePayrollWriter(), new NullAudit()).WriteAsync(Base(), [Ligne], Contexte());

        // Assert
        resultat.UpdatesWithoutRow.Should().Be(0);
        resultat.Message.Should().Be("Synchronisation des heures supplémentaires vers SAGE terminée.");
    }

    // ---------- STANDARD : marqueur T_PAIE_ENVOI ----------

    [Fact]
    public async Task StandardWrite_Success_RecordsTheSendOnceThenCompletesIt()
    {
        // Arrange
        var envois = new RecordingSendLog();

        // Act
        await new TestStandardWriter(new CapturingSagePayrollWriter(), new NullAudit(), sendLog: envois).WriteAsync(Base(), [Ligne, Ligne2], Contexte());

        // Assert
        var envoi = envois.Envois.Should().ContainSingle().Which;
        envoi.Send.SageDatabase.Should().Be("SAGE_TEST");
        envoi.Send.TypeBase.Should().Be("STANDARD");
        envoi.Send.PeriodFrom.Should().Be(new DateTime(2026, 10, 1));
        envoi.Send.PeriodTo.Should().Be(new DateTime(2026, 10, 31));
        envoi.Send.Status.Should().Be("EN_COURS");
        envoi.Send.Lines.Should().Be(7, "le marqueur est posé dès le premier salarié écrit");
        envoi.Status.Should().Be("SUCCES");
        envoi.Lines.Should().Be(14);
    }

    [Fact]
    public async Task StandardWrite_PartialFailure_IsRecordedAsSent()
    {
        // Arrange : erreur SQL après trois cumuls du premier salarié.
        var envois = new RecordingSendLog();
        var sut = new TestStandardWriter(new CapturingSagePayrollWriter(echecApres: 3), new NullAudit(), sendLog: envois);

        // Act
        var act = () => sut.WriteAsync(Base(), [Ligne], Contexte());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        var envoi = envois.Envois.Should().ContainSingle().Which;
        envoi.Status.Should().Be("ECHEC");
        envoi.Lines.Should().Be(3);
    }

    [Fact]
    public async Task StandardWrite_NothingWritten_RecordsNothing()
    {
        // Arrange : constante absente (aucune écriture), puis aucune ligne à envoyer.
        var envois = new RecordingSendLog();

        // Act
        var echec = () => new TestStandardWriter(new CapturingSagePayrollWriter(), new NullAudit(), sendLog: envois, sansCode: "HS01").WriteAsync(Base(), [Ligne], Contexte());
        await echec.Should().ThrowAsync<DomainException>();
        await new TestStandardWriter(new CapturingSagePayrollWriter(), new NullAudit(), sendLog: envois).WriteAsync(Base(), [], Contexte());
        await new TestStandardWriter(new CapturingSagePayrollWriter(lignes: _ => 0), new NullAudit(), sendLog: envois).WriteAsync(Base(), [Ligne], Contexte());

        // Assert
        envois.Envois.Should().BeEmpty();
    }

    [Fact]
    public async Task StandardWrite_MarkerCannotBeRecorded_StopsBeforeTheNextEmployee()
    {
        // Arrange
        var sage = new CapturingSagePayrollWriter();
        var envois = new RecordingSendLog { EchecEnregistrement = true };
        var audit = new CapturingAudit();

        // Act
        var act = () => new TestStandardWriter(sage, audit, sendLog: envois).WriteAsync(Base(), [Ligne, Ligne2], Contexte());

        // Assert : seul le premier salarié a été écrit, l'envoi est journalisé en échec.
        await act.Should().ThrowAsync<InvalidOperationException>();
        sage.Calls.Select(c => c.NumSal).Distinct().Should().Equal(101);
        audit.Statuts.Should().Equal("ECHEC");
    }

    [Fact]
    public async Task StandardWrite_MarkerCompletionFails_KeepsTheSuccessfulOutcome()
    {
        // Arrange
        var envois = new RecordingSendLog { EchecFin = true };
        var audit = new CapturingAudit();

        // Act
        var resultat = await new TestStandardWriter(new CapturingSagePayrollWriter(), audit, sendLog: envois).WriteAsync(Base(), [Ligne], Contexte());

        // Assert : la paie est écrite ; le marqueur reste EN_COURS (compte comme envoyé).
        resultat.CategoriesUpdated.Should().Be(7);
        audit.Statuts.Should().Equal("SUCCES");
        envois.Envois.Single().Status.Should().Be("EN_COURS");
    }

    // ---------- AUTRE : caractérisation ----------

    [Fact]
    public async Task OtherWrite_DefaultSelection_SendsTheHistoricalCommandsInOrder()
    {
        // Arrange
        var (sut, db, _, _) = Autre(selection: null);

        // Act
        var resultat = await sut.WriteAsync(BaseAutre(), [Ligne], Contexte());

        // Assert
        db.CountSql.Should().Equal("SELECT COUNT_BIG(1) FROM [dbo].[PAIE_CUMULS] WHERE [MatriculeEmploye] = @matricule");
        db.Updates.Should().Equal(
            ("UPDATE [dbo].[PAIE_CUMULS] SET [HeuresExo130] = @value WHERE [MatriculeEmploye] = @matricule", 1.5m, "MAT01"),
            ("UPDATE [dbo].[PAIE_CUMULS] SET [HeuresExo150] = @value WHERE [MatriculeEmploye] = @matricule", 2m, "MAT01"),
            ("UPDATE [dbo].[PAIE_CUMULS] SET [HeuresI130] = @value WHERE [MatriculeEmploye] = @matricule", 0.5m, "MAT01"),
            ("UPDATE [dbo].[PAIE_CUMULS] SET [HeuresI150] = @value WHERE [MatriculeEmploye] = @matricule", 1.25m, "MAT01"),
            ("UPDATE [dbo].[PAIE_CUMULS] SET [HeuresFerie] = @value WHERE [MatriculeEmploye] = @matricule", 3m, "MAT01"),
            ("UPDATE [dbo].[PAIE_CUMULS] SET [HeuresDimanche] = @value WHERE [MatriculeEmploye] = @matricule", 2.5m, "MAT01"),
            ("UPDATE [dbo].[PAIE_CUMULS] SET [HeuresNuit] = @value WHERE [MatriculeEmploye] = @matricule", 4m, "MAT01"));
        resultat.Should().Be(new PayrollWriteResultDto("SAGE_AUTRE", "AUTRE", 1, 7, "Synchronisation des heures supplémentaires terminée."));
    }

    [Fact]
    public async Task OtherWrite_NoConfiguration_IsIdenticalToTheWriterWithoutVariableService()
    {
        // Arrange
        var (historique, dbHistorique, _, _) = Autre(selection: null);
        var (configure, dbConfigure, _, _) = Autre(selection: null, service: VariableService(new InMemoryUnitOfWork()));

        // Act
        var avant = await historique.WriteAsync(BaseAutre(), [Ligne, Ligne2], Contexte());
        var apres = await configure.WriteAsync(BaseAutre(), [Ligne, Ligne2], Contexte());

        // Assert
        dbConfigure.Updates.Should().Equal(dbHistorique.Updates);
        dbConfigure.CountSql.Should().Equal(dbHistorique.CountSql);
        apres.Should().Be(avant);
    }

    // ---------- AUTRE : filtre ----------

    [Fact]
    public async Task OtherWrite_UncheckedVariable_IsNeitherRequiredValidatedNorWritten()
    {
        // Arrange : Dimanche décochée, sa ligne T_PAIE_ECRITURE absente et sa colonne inexistante.
        var (sut, db, _, _) = Autre(selection: Sans(PayrollVariableSelection.Dimanche), sansCategorie: "Dimanche");

        // Act
        var validation = await sut.ValidateAsync(BaseAutre(), [Ligne]);
        await sut.WriteAsync(BaseAutre(), [Ligne], Contexte());

        // Assert
        validation.Message.Should().Be("Configuration d'écriture valide.");
        db.Updates.Should().HaveCount(6);
        db.Updates.Should().NotContain(u => u.Sql.Contains("HeuresDimanche"));
    }

    [Fact]
    public async Task OtherWrite_NoCheckedOvertimeVariable_TouchesNothing()
    {
        // Arrange : adapté au lot C2 (avant : Retard seule) puis au lot C3 (avant : Absence seule, jamais envoyée ; elle est
        // désormais envoyable). Aucune variable cochée : rien à écrire.
        var (sut, db, journal, envois) = Autre(selection: PayrollVariableSelection.Avec([]));

        // Act
        var resultat = await sut.WriteAsync(BaseAutre(), [Ligne], Contexte());

        // Assert
        resultat.CategoriesUpdated.Should().Be(0);
        journal.Should().BeEmpty();
        db.Updates.Should().BeEmpty();
        envois.Envois.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherWrite_RetardCheckedWithoutDestination_IsRefusedBeforeAnyWrite()
    {
        // Arrange : Retard seule cochée, aucune ligne Retard dans T_PAIE_ECRITURE.
        var (sut, db, _, envois) = Autre(selection: PayrollVariableSelection.Avec([PayrollVariableSelection.Retard]));

        // Act
        var act = () => sut.WriteAsync(BaseAutre(), [Ligne], Contexte());

        // Assert
        await act.Should().ThrowAsync<DomainException>().WithMessage("*la catégorie Retard n'est pas configurée*");
        db.Updates.Should().BeEmpty();
        envois.Envois.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherWrite_RetardWithDestination_WritesTheDisplayedValueAfterTheSeven()
    {
        // Arrange : les sept + Retard vers la colonne decimal(18,2) HeuresRetard.
        var selection = PayrollVariableSelection.Avec([.. PayrollVariableSelection.HeuresSup, PayrollVariableSelection.Retard]);
        var (sut, db, _, _) = Autre(selection: selection, retard: new OtherPayrollColumn("decimal", 18, 2));

        // Act
        var resultat = await sut.WriteAsync(BaseAutre(), [Ligne with { Retard = 2.75m }], Contexte());

        // Assert
        resultat.CategoriesUpdated.Should().Be(8);
        db.Updates.Should().HaveCount(8);
        db.Updates.Last().Sql.Should().Contain("HeuresRetard");
        db.Updates.Last().Value.Should().Be(2.75m);
    }

    [Fact]
    public async Task OtherWrite_RetardDecimalIntoIntegerColumn_IsRefusedBeforeAnyWrite()
    {
        // Arrange : destination Retard de type int, valeur 2,75 h.
        var selection = PayrollVariableSelection.Avec([PayrollVariableSelection.Retard]);
        var (sut, db, _, envois) = Autre(selection: selection, retard: new OtherPayrollColumn("int", 10, 0));

        // Act
        var act = () => sut.WriteAsync(BaseAutre(), [Ligne with { Retard = 2.75m }], Contexte());

        // Assert
        await act.Should().ThrowAsync<DomainException>();
        db.Updates.Should().BeEmpty();
        envois.Envois.Should().BeEmpty();
    }

    // ---------- AUTRE : marqueur et atomicité ----------

    [Fact]
    public async Task OtherWrite_Success_RecordsTheMarkerBeforeCommittingThePayroll()
    {
        // Arrange
        var (sut, _, journal, envois) = Autre(selection: null);

        // Act
        await sut.WriteAsync(BaseAutre(), [Ligne], Contexte());

        // Assert
        journal.SkipWhile(e => e != "begin").Should().Equal(
            "begin", "update", "update", "update", "update", "update", "update", "update", "marqueur", "commit");
        var envoi = envois.Envois.Should().ContainSingle().Which;
        envoi.Status.Should().Be("SUCCES");
        envoi.Lines.Should().Be(7);
        envoi.Send.TypeBase.Should().Be("AUTRE");
    }

    [Fact]
    public async Task OtherWrite_UpdateDoesNotMatchOneRow_RollsBackWithoutMarker()
    {
        // Arrange : le deuxième salarié n'a aucune ligne pour Nuit.
        var (sut, db, journal, envois) = Autre(selection: null);
        db.Affected = (sql, matricule) => matricule == "MAT02" && sql.Contains("HeuresNuit") ? 0 : 1;

        // Act
        var act = () => sut.WriteAsync(BaseAutre(), [Ligne, Ligne2], Contexte());

        // Assert
        await act.Should().ThrowAsync<DomainException>();
        journal.Should().Contain("rollback").And.NotContain("commit");
        envois.Envois.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherWrite_MarkerCannotBeRecorded_NothingIsCommitted()
    {
        // Arrange
        var (sut, _, journal, envois) = Autre(selection: null);
        envois.EchecEnregistrement = true;

        // Act
        var act = () => sut.WriteAsync(BaseAutre(), [Ligne], Contexte());

        // Assert : sans marqueur, la paie est annulée.
        await act.Should().ThrowAsync<InvalidOperationException>();
        journal.Should().Contain("rollback").And.NotContain("commit");
        envois.Envois.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherWrite_CommitFails_KeepsTheMarkerAsUncertain()
    {
        // Arrange
        var (sut, db, journal, envois) = Autre(selection: null);
        db.EchecCommit = true;

        // Act
        var act = () => sut.WriteAsync(BaseAutre(), [Ligne], Contexte());

        // Assert : issue inconnue côté serveur, l'envoi compte comme envoyé.
        await act.Should().ThrowAsync<InvalidOperationException>();
        journal.Should().Contain("rollback");
        envois.Envois.Should().ContainSingle().Which.Status.Should().Be("INCERTAIN");
    }

    [Fact]
    public async Task OtherWrite_NoRows_CommitsWithoutMarker()
    {
        // Arrange
        var (sut, _, journal, envois) = Autre(selection: null);

        // Act
        await sut.WriteAsync(BaseAutre(), [], Contexte());

        // Assert
        journal.Should().Contain("commit");
        envois.Envois.Should().BeEmpty();
    }

    // ---------- Validation de configuration AUTRE ----------

    [Fact]
    public void ValidateConfigurationRows_InactiveCategoryMissing_IsAccepted()
    {
        // Arrange
        var configuration = Configuration().Where(c => c.Categorie != "Nuit").ToList();

        // Act
        var act = () => PayrollWritingConfiguration.ValidateConfigurationRows(configuration, Sans(PayrollVariableSelection.Nuit).HeuresSupActives);

        // Assert
        act.Should().NotThrow();
        FluentActions.Invoking(() => PayrollWritingConfiguration.ValidateConfigurationRows(configuration))
            .Should().ThrowExactly<DomainException>().WithMessage("*catégorie Nuit n'est pas configurée*");
    }

    [Fact]
    public void ValidateConfigurationRows_UnknownCategory_KeepsTheHistoricalMessage()
    {
        // Arrange
        var configuration = Configuration();
        configuration.Add(new PaieEcriture { BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "MatriculeEmploye", Categorie = "Autre", ColValeur = "X" });

        // Act
        var act = () => PayrollWritingConfiguration.ValidateConfigurationRows(configuration);

        // Assert
        act.Should().ThrowExactly<DomainException>().WithMessage("La configuration d'écriture contient des catégories inconnues.");
    }

    // ---------- C2b : remise à zéro des salariés du périmètre sans valeur ----------

    private static HsExoDto SansValeur(string matricule, int? numSal) =>
        new(0, matricule, numSal, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m) { SansValeur = true };

    [Fact]
    public async Task StandardWrite_EmployeeWithoutValue_ZeroIsWrittenForEveryCheckedVariable()
    {
        var sage = new CapturingSagePayrollWriter();

        var resultat = await new TestStandardWriter(sage, new NullAudit()).WriteAsync(Base(), [Ligne, SansValeur("MAT02", 102)], Contexte());

        sage.Calls.Where(c => c.NumSal == 102).Should().Equal(
            Enumerable.Range(1, 7).Select(op => (102, (short)op, 0m)));
        resultat.EmployeesUpdated.Should().Be(1);
        resultat.EmployeesReset.Should().Be(1);
        resultat.CategoriesUpdated.Should().Be(14);
        resultat.UpdatesWithoutRow.Should().Be(0);
        resultat.Message.Should().Be("Synchronisation des heures supplémentaires vers SAGE terminée."
            + " 1 salarié(s) sans valeur sur la période ont été remis à 0 pour les variables envoyées.");
    }

    [Fact]
    public async Task StandardWrite_UncheckedVariable_IsNeverZeroed()
    {
        var sage = new CapturingSagePayrollWriter();
        var writer = new TestStandardWriter(sage, new NullAudit(), new FixedVariableService(Sans(PayrollVariableSelection.Nuit)));

        await writer.WriteAsync(Base(), [SansValeur("MAT02", 102)], Contexte());

        sage.Calls.Select(c => c.Operand).Should().Equal((short)1, (short)2, (short)3, (short)4, (short)5, (short)6);
    }

    [Fact]
    public async Task StandardWrite_ZeroedEmployeeWithoutCumul_IsCountedNotAnError()
    {
        var sage = new CapturingSagePayrollWriter(lignes: op => op == 7 ? 0 : 1);

        var resultat = await new TestStandardWriter(sage, new NullAudit()).WriteAsync(Base(), [SansValeur("MAT02", 102)], Contexte());

        resultat.UpdatesWithoutRow.Should().Be(1);
        resultat.CategoriesUpdated.Should().Be(6);
        resultat.Message.Should().Contain("1 mise(s) à jour n'ont modifié aucune ligne T_CUMSAL (cumul absent dans SAGE pour le salarié), dont 1 pour des salariés remis à 0");
    }

    [Fact]
    public async Task StandardWrite_ZeroRowWithoutEmployeeNumber_IsNotTouched()
    {
        var sage = new CapturingSagePayrollWriter();

        var resultat = await new TestStandardWriter(sage, new NullAudit()).WriteAsync(Base(), [SansValeur("INCONNU", null)], Contexte());

        sage.Calls.Should().BeEmpty();
        resultat.EmployeesReset.Should().Be(0);
    }

    [Fact]
    public async Task StandardWrite_SentTwice_GivesTheSameResult()
    {
        var premier = new CapturingSagePayrollWriter();
        var second = new CapturingSagePayrollWriter();
        HsExoDto[] lignes = [Ligne, SansValeur("MAT02", 102)];

        var a = await new TestStandardWriter(premier, new NullAudit()).WriteAsync(Base(), lignes, Contexte());
        var b = await new TestStandardWriter(second, new NullAudit()).WriteAsync(Base(), lignes, Contexte());

        second.Calls.Should().Equal(premier.Calls);
        b.Should().Be(a);
    }

    [Fact]
    public async Task StandardWrite_ZeroRowWithoutEmployeeNumber_IsReportedAsNotReset_WithoutBlockingTheOthers()
    {
        var sage = new CapturingSagePayrollWriter();
        var audit = new CapturingAudit();

        var resultat = await new TestStandardWriter(sage, audit).WriteAsync(
            Base(), [Ligne, SansValeur("MAT02", null), SansValeur("MAT03", null)], Contexte());

        sage.Calls.Select(c => c.NumSal).Distinct().Should().Equal(101);
        resultat.EmployeesUpdated.Should().Be(1);
        resultat.EmployeesNotReset.Should().Be(2);
        resultat.Message.Should().Contain("Attention : 2 salarié(s) sans valeur n'ont PAS été remis à 0")
            .And.Contain("leurs anciens cumuls restent dans SAGE");
        var journal = audit.Audits.Should().ContainSingle().Which;
        journal.EmployeesNotReset.Should().Be(2);
        journal.Status.Should().Be("SUCCES");
    }

    [Fact]
    public async Task StandardWrite_AllZeroed_HasNoNotResetWarning()
    {
        var resultat = await new TestStandardWriter(new CapturingSagePayrollWriter(), new NullAudit())
            .WriteAsync(Base(), [SansValeur("MAT02", 102)], Contexte());

        resultat.EmployeesNotReset.Should().Be(0);
        resultat.Message.Should().NotContain("PAS été remis à 0");
    }

    [Fact]
    public async Task OtherWrite_EmployeeWithoutValue_ZeroIsWrittenInTheSameTransactionAfterThePreflight()
    {
        var (sut, db, journal, _) = Autre(selection: null);

        var resultat = await sut.WriteAsync(BaseAutre(), [Ligne, SansValeur("MAT02", null)], Contexte());

        db.Updates.Where(u => u.Matricule == "MAT02").Select(u => u.Value).Should().OnlyContain(v => v == 0m).And.HaveCount(7);
        resultat.EmployeesUpdated.Should().Be(1);
        resultat.EmployeesReset.Should().Be(1);
        resultat.CategoriesUpdated.Should().Be(14);
        journal.IndexOf("begin").Should().BeGreaterThan(journal.IndexOf("colonnes:dbo.PAIE_CUMULS"));
        journal.Last().Should().Be("commit");
        journal.Count(j => j == "begin").Should().Be(1);
    }

    [Fact]
    public async Task OtherWrite_UncheckedVariable_IsNeverZeroed()
    {
        var (sut, db, _, _) = Autre(Sans(PayrollVariableSelection.Nuit));

        await sut.WriteAsync(BaseAutre(), [SansValeur("MAT02", null)], Contexte());

        db.Updates.Should().HaveCount(6).And.NotContain(u => u.Sql.Contains("HeuresNuit"));
    }

    [Fact]
    public async Task OtherWrite_ZeroedEmployeeAbsentFromThePayrollTable_IsSkippedWithoutError()
    {
        var (sut, db, _, envois) = Autre(selection: null);
        db.LignesParMatricule = matricule => matricule == "ABSENT" ? 0 : 1;

        var resultat = await sut.WriteAsync(BaseAutre(), [Ligne, SansValeur("ABSENT", null)], Contexte());

        db.Updates.Should().OnlyContain(u => u.Matricule == "MAT01");
        resultat.UpdatesWithoutRow.Should().Be(1);
        resultat.EmployeesReset.Should().Be(0);
        resultat.Message.Should().Contain("1 salarié(s) sans valeur absent(s) de la table de paie n'ont pas été touchés.");
        envois.Envois.Should().ContainSingle();
    }

    [Fact]
    public async Task OtherWrite_ZeroUpdateMatchingNoRow_RollsBackEverything()
    {
        var (sut, db, journal, envois) = Autre(selection: null);
        db.Affected = (_, matricule) => matricule == "MAT02" ? 0 : 1;

        var act = () => sut.WriteAsync(BaseAutre(), [Ligne, SansValeur("MAT02", null)], Contexte());

        await act.Should().ThrowAsync<DomainException>();
        journal.Should().Contain("rollback").And.NotContain("commit");
        envois.Envois.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherWrite_SentTwice_GivesTheSameUpdates()
    {
        var (premier, db1, _, _) = Autre(selection: null);
        var (second, db2, _, _) = Autre(selection: null);
        HsExoDto[] lignes = [Ligne, SansValeur("MAT02", null)];

        var a = await premier.WriteAsync(BaseAutre(), lignes, Contexte());
        var b = await second.WriteAsync(BaseAutre(), lignes, Contexte());

        db2.Updates.Should().Equal(db1.Updates);
        b.Should().Be(a);
    }

    // ---------- Aides ----------

    private static PayrollVariableSelection Sans(string variable) =>
        PayrollVariableSelection.Avec(PayrollVariableSelection.HeuresSup.Where(v => v != variable));

    private static BddSage Base() => new() { NomBd = "SAGE_TEST", TypeBase = "STANDARD" };
    private static BddSage BaseAutre() => new() { NomBd = "SAGE_AUTRE", TypeBase = "AUTRE" };
    private static PayrollSyncContext Contexte() => new(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), "rh", Guid.NewGuid());

    private static SagePayrollTarget Cible(string? sansCode = null) => new("test",
        Enumerable.Range(1, 7)
            .Select(i => new ConstanteSage { CodeConstante = $"HS0{i}", CodeOperande1 = (short)i })
            .Append(new ConstanteSage { CodeConstante = "RET01", CodeOperande1 = 8 })
            .Where(c => c.CodeConstante != sansCode)
            .ToArray(),
        new OvertimeSageCodes());

    private static List<PaieEcriture> Configuration(string? sansCategorie = null) => PayrollWritingConfiguration.Categories
        .Where(c => c != sansCategorie)
        .Select(c => new PaieEcriture { BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "MatriculeEmploye", Categorie = c, ColValeur = $"Heures{c}" })
        .ToList();

    private static (OtherPayrollWriter Sut, FakeOtherPayrollDatabase Db, List<string> Journal, RecordingSendLog Envois) Autre(
        PayrollVariableSelection? selection, IPayrollVariableService? service = null, string? sansCategorie = null,
        OtherPayrollColumn? retard = null)
    {
        var journal = new List<string>();
        var db = new FakeOtherPayrollDatabase(journal);
        db.Columns["MatriculeEmploye"] = new OtherPayrollColumn("nvarchar", null, null);
        foreach (var c in PayrollWritingConfiguration.Categories.Where(c => c != sansCategorie))
            db.Columns[$"Heures{c}"] = new OtherPayrollColumn("decimal", 18, 4);
        var uow = new InMemoryUnitOfWork();
        uow.Get<PaieEcriture>().Items.AddRange(Configuration(sansCategorie));
        if (retard is not null)
        {
            db.Columns["HeuresRetard"] = retard;
            uow.Get<PaieEcriture>().Items.Add(new PaieEcriture
            {
                BddSage = "SAGE_AUTRE", TableCible = "PAIE_CUMULS", ColMatricule = "MatriculeEmploye", Categorie = "Retard", ColValeur = "HeuresRetard"
            });
        }
        var envois = new RecordingSendLog(journal);
        var variables = service ?? (selection is null ? null : new FixedVariableService(selection));
        var sut = new OtherPayrollWriter(uow, Mock.Of<ITenantConnectionService>(), new NullAudit(),
            NullLogger<OtherPayrollWriter>.Instance, variables, envois, _ => Task.FromResult<IOtherPayrollDatabase>(db));
        return (sut, db, journal, envois);
    }

    /// <summary>Service réel de variables sur une unité de travail en mémoire (sans droit, lecture de la sélection).</summary>
    internal static PayrollVariableService VariableService(InMemoryUnitOfWork uow) => new(
        uow, Mock.Of<ITenantConnectionService>(), Mock.Of<Application.Abstractions.DataSources.IDatabaseConfigurationAuthorizer>(),
        new TestCurrentUser(1, 1), new RecordingSendLog(), Mock.Of<Application.Abstractions.Settings.ICodeConstanteService>(),
        Mock.Of<Application.Abstractions.DataSources.IExternalDiscoveryService>(), new PayrollVariableCache(),
        new Infrastructure.Services.Closing.ClosingAnalysisCache(), new Infrastructure.Services.Closing.ClosingChecklistCache(),
        TimeProvider.System, NullLogger<PayrollVariableService>.Instance);

    private sealed class TestStandardWriter(
        ISagePayrollWriter sageWriter, IAuditTrail auditTrail, IPayrollVariableService? variables = null,
        IPayrollSendLog? sendLog = null, string? sansCode = null)
        : StandardPayrollWriter(sageWriter, null!, null!, null!, auditTrail, variables, sendLog)
    {
        protected override Task<SagePayrollTarget> GetInputAsync(CancellationToken ct) => Task.FromResult(Cible(sansCode));
    }

    private sealed class NullAudit : IAuditTrail
    {
        public Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CapturingAudit : IAuditTrail
    {
        public List<string> Statuts { get; } = new();
        public List<PayrollSyncAudit> Audits { get; } = new();

        public Task WritePayrollSyncAsync(PayrollSyncAudit audit, CancellationToken cancellationToken = default)
        {
            Audits.Add(audit);
            Statuts.Add(audit.Status);
            return Task.CompletedTask;
        }
    }
}
