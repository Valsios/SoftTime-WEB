/* =====================================================================================
   SoftTime — Migration 009 : instantanés des envois en paie (empreinte SHA-256) — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : - dbo.T_PAIE_INSTANTANE : entête de l'instantané d'un envoi réussi (un par ligne de T_PAIE_ENVOI) :
               base, période, type de base, date et auteur de l'envoi, filtre de l'envoi, version du format,
               empreinte SHA-256, nombre de lignes, mises à jour sans ligne, salariés non remis à 0.
             - dbo.T_PAIE_INSTANTANE_LIGNE : valeurs RÉELLEMENT écrites dans la paie par cet envoi (salarié, variable,
               destination, unité, valeur, indicateur « zéro de remise à 0 »).
             - Immuabilité : déclencheurs INSTEAD OF UPDATE, DELETE sur les deux tables (toute modification ou
               suppression est refusée, erreur 51991) ; clé étrangère vers T_PAIE_ENVOI (un envoi qui a un instantané
               ne peut plus être supprimé). L'application n'a aucun chemin de modification ; l'empreinte détecte une
               altération faite en contournant les déclencheurs (administrateur de la base).
   Règles  : - chaque objet n'est créé que s'il n'existe pas ; aucune donnée n'est insérée ni modifiée ;
             - aucune instruction destructive, aucun secret ;
             - les envois antérieurs à ce script n'ont pas d'instantané (aucune reprise de l'historique).
   Production sans AutoMigrate : exécuter ce fichier une fois sur la base SoftTime (sqlcmd -d <base> -i 009_instantanes_paie.sql).
   ===================================================================================== */

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.T_PAIE_INSTANTANE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_PAIE_INSTANTANE (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_T_PAIE_INSTANTANE PRIMARY KEY,
        ENVOI_ID int NOT NULL CONSTRAINT FK_T_PAIE_INSTANTANE_ENVOI REFERENCES dbo.T_PAIE_ENVOI (ID),
        BDD_SAGE nvarchar(128) NOT NULL,
        PERIODE_DEBUT date NOT NULL,
        PERIODE_FIN date NOT NULL,
        TYPE_BASE nvarchar(16) NOT NULL,
        ENVOYE_LE datetime2 NOT NULL,
        ENVOYE_PAR nvarchar(256) NULL,
        DEPARTEMENT nvarchar(128) NULL,
        MATRICULE_DE nvarchar(64) NULL,
        MATRICULE_A nvarchar(64) NULL,
        AVEC_FILTRE bit NOT NULL,
        REMISE_A_ZERO bit NOT NULL,
        HEURES_PAR_JOUR decimal(9,4) NOT NULL,
        VERSION_FORMAT int NOT NULL,
        EMPREINTE char(64) NOT NULL,
        NB_LIGNES int NOT NULL,
        NB_SANS_LIGNE int NOT NULL,
        NB_NON_REMIS int NOT NULL,
        CREE_LE datetime2 NOT NULL
    );
    PRINT N'009 : table T_PAIE_INSTANTANE créée.';
END
ELSE
    PRINT N'009 : table T_PAIE_INSTANTANE déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_PAIE_INSTANTANE_ENVOI' AND object_id = OBJECT_ID(N'dbo.T_PAIE_INSTANTANE'))
    CREATE UNIQUE INDEX UX_T_PAIE_INSTANTANE_ENVOI ON dbo.T_PAIE_INSTANTANE (ENVOI_ID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PAIE_INSTANTANE_BDD_PERIODE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_INSTANTANE'))
    CREATE INDEX IX_T_PAIE_INSTANTANE_BDD_PERIODE ON dbo.T_PAIE_INSTANTANE (BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN);
GO

IF OBJECT_ID(N'dbo.T_PAIE_INSTANTANE_LIGNE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_PAIE_INSTANTANE_LIGNE (
        ID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_T_PAIE_INSTANTANE_LIGNE PRIMARY KEY,
        INSTANTANE_ID int NOT NULL CONSTRAINT FK_T_PAIE_INSTANTANE_LIGNE_INSTANTANE REFERENCES dbo.T_PAIE_INSTANTANE (ID),
        MATRICULE nvarchar(64) NOT NULL,
        NUM_SAL int NULL,
        VARIABLE nvarchar(32) NOT NULL,
        DESTINATION nvarchar(128) NOT NULL,
        UNITE nvarchar(1) NOT NULL,
        VALEUR decimal(18,4) NOT NULL,
        REMISE_A_ZERO bit NOT NULL
    );
    PRINT N'009 : table T_PAIE_INSTANTANE_LIGNE créée.';
END
ELSE
    PRINT N'009 : table T_PAIE_INSTANTANE_LIGNE déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PAIE_INSTANTANE_LIGNE_INSTANTANE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_INSTANTANE_LIGNE'))
    CREATE INDEX IX_T_PAIE_INSTANTANE_LIGNE_INSTANTANE ON dbo.T_PAIE_INSTANTANE_LIGNE (INSTANTANE_ID);
GO

IF OBJECT_ID(N'dbo.TR_T_PAIE_INSTANTANE_IMMUABLE', N'TR') IS NULL
    EXEC (N'CREATE TRIGGER dbo.TR_T_PAIE_INSTANTANE_IMMUABLE ON dbo.T_PAIE_INSTANTANE INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51991, N''Instantané d''''envoi en paie immuable : modification et suppression interdites (T_PAIE_INSTANTANE).'', 1;
END');
GO

IF OBJECT_ID(N'dbo.TR_T_PAIE_INSTANTANE_LIGNE_IMMUABLE', N'TR') IS NULL
    EXEC (N'CREATE TRIGGER dbo.TR_T_PAIE_INSTANTANE_LIGNE_IMMUABLE ON dbo.T_PAIE_INSTANTANE_LIGNE INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51991, N''Instantané d''''envoi en paie immuable : modification et suppression interdites (T_PAIE_INSTANTANE_LIGNE).'', 1;
END');
GO
