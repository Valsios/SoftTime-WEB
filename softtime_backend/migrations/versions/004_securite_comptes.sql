/* =====================================================================================
   SoftTime — Migration 004 : sécurité des comptes — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : - dbo.T_REFRESH_TOKEN : jetons de rafraîchissement (empreinte SHA-256 seulement, jamais
               la valeur), révocables, regroupés par famille pour détecter une réutilisation ;
             - dbo.T_RESPONSABLE : compteur d'échecs de connexion et date de fin de verrouillage.
   Règles  : - chaque objet n'est créé que s'il n'existe pas ; aucune colonne existante n'est modifiée ;
             - aucune instruction destructive, aucun secret.
   ===================================================================================== */

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.T_REFRESH_TOKEN', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_REFRESH_TOKEN (
        ID                bigint IDENTITY(1,1) NOT NULL,
        IDRESPONSABLE     numeric(18,0) NOT NULL,
        TOKEN_HASH        char(64) NOT NULL,
        FAMILLE           uniqueidentifier NOT NULL,
        DATE_CREATION     datetime2 NOT NULL,
        DATE_EXPIRATION   datetime2 NOT NULL,
        DATE_REVOCATION   datetime2 NULL,
        REMPLACE_PAR      char(64) NULL,
        CONSTRAINT PK_T_REFRESH_TOKEN PRIMARY KEY (ID)
    );
    PRINT N'004 : dbo.T_REFRESH_TOKEN créée.';
END
ELSE
    PRINT N'004 : dbo.T_REFRESH_TOKEN existe déjà, aucune colonne modifiée.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_REFRESH_TOKEN_Hash' AND object_id = OBJECT_ID(N'dbo.T_REFRESH_TOKEN'))
    EXEC (N'CREATE UNIQUE INDEX UX_T_REFRESH_TOKEN_Hash ON dbo.T_REFRESH_TOKEN (TOKEN_HASH);');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_REFRESH_TOKEN_Responsable' AND object_id = OBJECT_ID(N'dbo.T_REFRESH_TOKEN'))
    EXEC (N'CREATE INDEX IX_T_REFRESH_TOKEN_Responsable ON dbo.T_REFRESH_TOKEN (IDRESPONSABLE, DATE_REVOCATION);');
GO

IF COL_LENGTH(N'dbo.T_RESPONSABLE', N'TENTATIVES_ECHOUEES') IS NULL
BEGIN
    ALTER TABLE dbo.T_RESPONSABLE ADD TENTATIVES_ECHOUEES int NOT NULL
        CONSTRAINT DF_T_RESPONSABLE_TENTATIVES_ECHOUEES DEFAULT (0);
    PRINT N'004 : colonne T_RESPONSABLE.TENTATIVES_ECHOUEES ajoutée.';
END
GO

IF COL_LENGTH(N'dbo.T_RESPONSABLE', N'VERROUILLE_JUSQUA') IS NULL
BEGIN
    ALTER TABLE dbo.T_RESPONSABLE ADD VERROUILLE_JUSQUA datetime2 NULL;
    PRINT N'004 : colonne T_RESPONSABLE.VERROUILLE_JUSQUA ajoutée.';
END
GO
