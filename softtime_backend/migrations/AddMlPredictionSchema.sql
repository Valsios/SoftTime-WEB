-- ---------------------------------------------------------------------------
-- Script idempotent genere par dotnet ef migrations script --idempotent.
-- SET QUOTED_IDENTIFIER ON est requis pour l'index filtre de ml.Modele ;
-- sqlcmd l'active aussi avec l'option -I.
-- ---------------------------------------------------------------------------
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    IF SCHEMA_ID(N'ml') IS NULL EXEC(N'CREATE SCHEMA [ml];');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE TABLE [ml].[Modele] (
        [Id] int NOT NULL IDENTITY,
        [BddSage] nvarchar(128) NOT NULL,
        [TypeModele] nvarchar(32) NOT NULL,
        [Version] nvarchar(32) NOT NULL,
        [Algorithme] nvarchar(64) NOT NULL,
        [Statut] nvarchar(16) NOT NULL,
        [EstActif] bit NOT NULL,
        [EntraineLe] datetime2 NOT NULL,
        [PeriodeDebut] datetime2 NOT NULL,
        [PeriodeFin] datetime2 NOT NULL,
        [NbLignes] int NOT NULL,
        [NbPositifs] int NULL,
        [MetriquesJson] nvarchar(max) NULL,
        [SchemaHash] nvarchar(64) NOT NULL,
        [ModeleBinaire] varbinary(max) NULL,
        [Commentaire] nvarchar(512) NULL,
        CONSTRAINT [PK_Modele] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE TABLE [ml].[SalarieJour] (
        [Id] bigint NOT NULL IDENTITY,
        [BddSage] nvarchar(128) NOT NULL,
        [Matricule] nvarchar(64) NOT NULL,
        [Date] datetime2 NOT NULL,
        [EstJourPlanifie] bit NOT NULL,
        [EstOff] bit NOT NULL,
        [EstFerie] bit NOT NULL,
        [EstWeekend] bit NOT NULL,
        [EstConge] bit NOT NULL,
        [PlanningConnu] bit NOT NULL,
        [HeuresPlanifiees] decimal(6,2) NULL,
        [HeuresTravaillees] decimal(6,2) NULL,
        [RetardMinutes] int NULL,
        [AbsenceNonJustifiee] bit NOT NULL,
        [AbsenceDeclaree] bit NOT NULL,
        [AbsenceHeures] decimal(6,2) NULL,
        [JourSemaine] tinyint NOT NULL,
        [SemaineIso] tinyint NOT NULL,
        [AnneeIso] smallint NOT NULL,
        [Mois] tinyint NOT NULL,
        [JourDuMois] tinyint NOT NULL,
        [VeilleFerie] bit NOT NULL,
        [LendemainFerie] bit NOT NULL,
        [DebutMois] bit NOT NULL,
        [FinMois] bit NOT NULL,
        [CategorieId] int NULL,
        [Departement] nvarchar(128) NULL,
        [HeuresSemaineContrat] decimal(6,2) NULL,
        [MultiPointage] bit NOT NULL,
        [AncienneteJours] int NULL,
        [Absences7j] int NOT NULL,
        [Absences30j] int NOT NULL,
        [Absences90j] int NOT NULL,
        [TauxAbsence30j] float NOT NULL,
        [Retards30j] int NOT NULL,
        [JoursTravailles7j] int NOT NULL,
        [JoursDepuisDerniereAbsence] int NULL,
        [HeuresSupp4Semaines] decimal(8,2) NOT NULL,
        [TauxAbsenceEquipe30j] float NOT NULL,
        [EffectifPrevuJour] int NOT NULL,
        [CongesPrevusJour] int NOT NULL,
        [SourceGenereeLe] datetime2 NOT NULL,
        [VersionSchema] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_SalarieJour] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE TABLE [ml].[SalarieSemaine] (
        [Id] bigint NOT NULL IDENTITY,
        [BddSage] nvarchar(128) NOT NULL,
        [Matricule] nvarchar(64) NOT NULL,
        [SemaineDebut] datetime2 NOT NULL,
        [SemaineIso] tinyint NOT NULL,
        [AnneeIso] smallint NOT NULL,
        [HeuresPlanifiees] decimal(8,2) NOT NULL,
        [HeuresTravaillees] decimal(8,2) NOT NULL,
        [NbJoursPlanifies] int NOT NULL,
        [NbJoursTravailles] int NOT NULL,
        [CongeJours] decimal(4,1) NOT NULL,
        [HS] decimal(8,2) NOT NULL,
        [HSValidee] bit NOT NULL,
        [Nuit] decimal(8,2) NOT NULL,
        [Dimanche] decimal(8,2) NOT NULL,
        [Ferie] decimal(8,2) NOT NULL,
        [RetardMinutes] int NOT NULL,
        [AbsenceHeures] decimal(8,2) NOT NULL,
        [HsMoyenne4Semaines] decimal(8,2) NOT NULL,
        [HsMoyenne8Semaines] decimal(8,2) NOT NULL,
        [Absences4Semaines] int NOT NULL,
        [Retards4Semaines] int NOT NULL,
        [HeuresTravaillees4Semaines] decimal(8,2) NOT NULL,
        [CategorieId] int NULL,
        [Departement] nvarchar(128) NULL,
        [HeuresSemaineContrat] decimal(6,2) NULL,
        [MultiPointage] bit NOT NULL,
        [SourceGenereeLe] datetime2 NOT NULL,
        [VersionSchema] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_SalarieSemaine] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE TABLE [ml].[Prediction] (
        [Id] bigint NOT NULL IDENTITY,
        [BddSage] nvarchar(128) NOT NULL,
        [Matricule] nvarchar(64) NOT NULL,
        [TypeModele] nvarchar(32) NOT NULL,
        [Cible] datetime2 NOT NULL,
        [Probabilite] float NULL,
        [ValeurPrevue] decimal(8,2) NULL,
        [BorneBasse] decimal(8,2) NULL,
        [BorneHaute] decimal(8,2) NULL,
        [NiveauRisque] tinyint NOT NULL,
        [ModeleId] int NOT NULL,
        [GenereLe] datetime2 NOT NULL,
        CONSTRAINT [PK_Prediction] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Prediction_Modele_ModeleId] FOREIGN KEY ([ModeleId]) REFERENCES [ml].[Modele] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Modele_BddSage_TypeModele_EstActif] ON [ml].[Modele] ([BddSage], [TypeModele], [EstActif]) WHERE [EstActif] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE INDEX [IX_Prediction_BddSage_TypeModele_Cible] ON [ml].[Prediction] ([BddSage], [TypeModele], [Cible]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Prediction_BddSage_TypeModele_Matricule_Cible_ModeleId] ON [ml].[Prediction] ([BddSage], [TypeModele], [Matricule], [Cible], [ModeleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE INDEX [IX_Prediction_ModeleId] ON [ml].[Prediction] ([ModeleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE INDEX [IX_SalarieJour_BddSage_Date] ON [ml].[SalarieJour] ([BddSage], [Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SalarieJour_BddSage_Matricule_Date] ON [ml].[SalarieJour] ([BddSage], [Matricule], [Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SalarieSemaine_BddSage_Matricule_SemaineDebut] ON [ml].[SalarieSemaine] ([BddSage], [Matricule], [SemaineDebut]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    CREATE INDEX [IX_SalarieSemaine_BddSage_SemaineDebut] ON [ml].[SalarieSemaine] ([BddSage], [SemaineDebut]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030124102_AddMlPredictionSchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030124102_AddMlPredictionSchema', N'8.0.11');
END;
GO

COMMIT;
GO

