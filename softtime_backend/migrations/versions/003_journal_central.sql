/* =====================================================================================
   SoftTime — Migration 003 : journal central des modifications (suivi_JOURNAL) — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : crée dbo.suivi_JOURNAL (entité suivi_JOURNAL) et ses index. Remplace la création
             au démarrage par AuditJournalConfiguration et le script manuel create_audit_journal.sql
             (qui n'est plus nécessaire).
   Règles  : - la table n'est créée que si elle n'existe pas ; sur une table existante AUCUNE colonne
               n'est modifiée ;
             - chaque index est créé seulement s'il n'existe pas (nom testé) et seulement si ses
               colonnes sont indexables ; sinon il est IGNORÉ et un PRINT l'indique ;
             - seuls des index NON uniques sont ajoutés ;
             - suivi_SUIVI, suivi_TBLCLN, les déclencheurs et T_AUDIT_DROIT ne sont pas touchés ;
             - aucune instruction destructive, aucun secret.
   ===================================================================================== */

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.suivi_JOURNAL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.suivi_JOURNAL (
        Id               bigint IDENTITY(1,1) NOT NULL,
        DATE_UTC         datetime2 NOT NULL,
        TABLES           nvarchar(128) NOT NULL,
        CLES             nvarchar(450) NULL,
        COLONNES         nvarchar(128) NULL,
        ACTIONS          nvarchar(32) NOT NULL,
        ANCIENS          nvarchar(max) NULL,
        NOUVEAUX         nvarchar(max) NULL,
        USERS            nvarchar(256) NOT NULL,
        BASE_SAGE        nvarchar(128) NULL,
        BASE_POINTEUSE   nvarchar(128) NULL,
        CORRELATIONS     uniqueidentifier NOT NULL,
        SOURCE           nvarchar(64) NOT NULL,
        CONSTRAINT PK_suivi_JOURNAL PRIMARY KEY (Id)
    );
    PRINT N'003 : dbo.suivi_JOURNAL créée.';
END
ELSE
    PRINT N'003 : dbo.suivi_JOURNAL existe déjà, aucune colonne modifiée.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_Date' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    EXEC (N'CREATE INDEX IX_suivi_JOURNAL_Date ON dbo.suivi_JOURNAL (DATE_UTC DESC, Id DESC);');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_Base_Date' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    EXEC (N'CREATE INDEX IX_suivi_JOURNAL_Base_Date ON dbo.suivi_JOURNAL (BASE_SAGE, DATE_UTC DESC, Id DESC);');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_Table_Action_Date' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    EXEC (N'CREATE INDEX IX_suivi_JOURNAL_Table_Action_Date ON dbo.suivi_JOURNAL (TABLES, ACTIONS, DATE_UTC DESC, Id DESC);');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_Correlation' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    EXEC (N'CREATE INDEX IX_suivi_JOURNAL_Correlation ON dbo.suivi_JOURNAL (CORRELATIONS, Id);');
GO

-- Index par utilisateur : seulement si USERS est borné (pas nvarchar(max)) et tient dans la clé (1700 octets).
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_User_Date' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    PRINT N'003 : IX_suivi_JOURNAL_User_Date existe déjà, rien à faire.';
ELSE IF EXISTS (SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.suivi_JOURNAL') AND name = N'USERS'
                  AND max_length > 0 AND max_length <= 1700)
    EXEC (N'CREATE INDEX IX_suivi_JOURNAL_User_Date ON dbo.suivi_JOURNAL (USERS, DATE_UTC DESC, Id DESC);');
ELSE
    PRINT N'003 : USERS non indexable (nvarchar(max) ou trop large), index IX_suivi_JOURNAL_User_Date ignoré.';
GO

-- Index par objet (clé de la ligne modifiée) : mêmes conditions sur CLES.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_Cles_Date' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    PRINT N'003 : IX_suivi_JOURNAL_Cles_Date existe déjà, rien à faire.';
ELSE IF EXISTS (SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.suivi_JOURNAL') AND name = N'CLES'
                  AND max_length > 0 AND max_length <= 1700)
    EXEC (N'CREATE INDEX IX_suivi_JOURNAL_Cles_Date ON dbo.suivi_JOURNAL (CLES, DATE_UTC DESC, Id DESC);');
ELSE
    PRINT N'003 : CLES non indexable (nvarchar(max) ou trop large), index IX_suivi_JOURNAL_Cles_Date ignoré.';
GO
