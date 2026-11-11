/* =====================================================================================
   SoftTime — Migration 006 : registre des transformations de données — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : dbo.T_MIGRATION_DONNEES : une ligne par transformation de données faite par l'application
             au démarrage (par exemple DROITS_ECARTS_V1 : droits des comptes stockés en écarts au rôle,
             rôles sans ligne convertis en droits détaillés). Une transformation inscrite n'est jamais rejouée.
   Règles  : - la table n'est créée que si elle n'existe pas ;
             - aucune instruction destructive, aucun secret.
   ===================================================================================== */

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.T_MIGRATION_DONNEES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_MIGRATION_DONNEES (
        CODE              nvarchar(100) NOT NULL,
        DATE_APPLICATION  datetime2     NOT NULL,
        CONSTRAINT PK_T_MIGRATION_DONNEES PRIMARY KEY (CODE)
    );
    PRINT N'006 : dbo.T_MIGRATION_DONNEES créée.';
END
ELSE
    PRINT N'006 : dbo.T_MIGRATION_DONNEES existe déjà.';
GO
