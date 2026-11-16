/* =====================================================================================
   SoftTime — Migration 007 : variables envoyées à la paie et marqueur d'envoi — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : - dbo.T_PAIE_VARIABLE : choix, par base SAGE, des variables envoyées à la paie
               (une ligne par base et par variable). Une base SANS ligne garde le comportement
               historique : les 7 variables d'heures supplémentaires envoyées, Retard et Absence non.
             - dbo.T_PAIE_ENVOI : marqueur « période envoyée en paie », écrit par les writers de paie
               (indépendant du journal central). Sert à refuser une modification de la configuration
               quand une période envoyée recouvre le mois civil en cours.
   Règles  : - chaque objet n'est créé que s'il n'existe pas ; aucune donnée n'est insérée ni modifiée ;
             - aucune instruction destructive, aucun secret ;
             - les envois antérieurs à ce script ne sont pas repris (aucune reprise de l'historique).
   ===================================================================================== */

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.T_PAIE_VARIABLE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_PAIE_VARIABLE (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_T_PAIE_VARIABLE PRIMARY KEY,
        BDD_SAGE nvarchar(128) NOT NULL,
        VARIABLE nvarchar(32) NOT NULL,
        ACTIF bit NOT NULL,
        CODE_PAIE nvarchar(32) NULL,
        UNITE nvarchar(1) NULL,
        COL_CIBLE nvarchar(128) NULL,
        MODIFIE_LE datetime2 NULL,
        MODIFIE_PAR nvarchar(256) NULL
    );
    PRINT N'007 : table T_PAIE_VARIABLE créée.';
END
ELSE
    PRINT N'007 : table T_PAIE_VARIABLE déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_PAIE_VARIABLE_BDD_VARIABLE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_VARIABLE'))
    CREATE UNIQUE INDEX UX_T_PAIE_VARIABLE_BDD_VARIABLE ON dbo.T_PAIE_VARIABLE (BDD_SAGE, VARIABLE);
GO

IF OBJECT_ID(N'dbo.T_PAIE_ENVOI', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_PAIE_ENVOI (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_T_PAIE_ENVOI PRIMARY KEY,
        BDD_SAGE nvarchar(128) NOT NULL,
        PERIODE_DEBUT date NOT NULL,
        PERIODE_FIN date NOT NULL,
        ENVOYE_LE datetime2 NOT NULL,
        ENVOYE_PAR nvarchar(256) NULL,
        STATUT nvarchar(16) NOT NULL,
        LIGNES int NOT NULL,
        TYPE_BASE nvarchar(16) NOT NULL
    );
    PRINT N'007 : table T_PAIE_ENVOI créée.';
END
ELSE
    PRINT N'007 : table T_PAIE_ENVOI déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PAIE_ENVOI_BDD_PERIODE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_ENVOI'))
    CREATE INDEX IX_T_PAIE_ENVOI_BDD_PERIODE ON dbo.T_PAIE_ENVOI (BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN);
GO
