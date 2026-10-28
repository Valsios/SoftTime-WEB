/*
   AuditLog central — table applicative indépendante de suivi_SUIVI.
   IMPORTANT : ne modifie pas suivi_SUIVI ni ses triggers historiques.
   Script idempotent : à relire avant exécution sur chaque base SoftTime cible.
*/
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
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_Date' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    CREATE INDEX IX_suivi_JOURNAL_Date ON dbo.suivi_JOURNAL (DATE_UTC DESC, Id DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_Base_Date' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    CREATE INDEX IX_suivi_JOURNAL_Base_Date ON dbo.suivi_JOURNAL (BASE_SAGE, DATE_UTC DESC, Id DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_Table_Action_Date' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    CREATE INDEX IX_suivi_JOURNAL_Table_Action_Date ON dbo.suivi_JOURNAL (TABLES, ACTIONS, DATE_UTC DESC, Id DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_JOURNAL_Correlation' AND object_id = OBJECT_ID(N'dbo.suivi_JOURNAL'))
    CREATE INDEX IX_suivi_JOURNAL_Correlation ON dbo.suivi_JOURNAL (CORRELATIONS, Id);
GO
