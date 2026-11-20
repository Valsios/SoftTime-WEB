/* =====================================================================================
   SoftTime — Migration 010 : clôture de paie (période figée, sans déblocage) — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : - dbo.T_PAIE_CLOTURE : une ligne par période clôturée d'une base SAGE (base, début, fin), avec la date,
               l'auteur, l'envoi et l'instantané figés (T_PAIE_ENVOI, T_PAIE_INSTANTANE), l'empreinte SHA-256, le statut
               du Contrôle avant paie au moment de la clôture et son résumé.
             - Unicité sur (BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN) ; le non-recouvrement de deux périodes clôturées d'une
               même base est contrôlé par l'application (sous verrou applicatif).
             - Immuabilité : déclencheur INSTEAD OF UPDATE, DELETE (erreur 51992) : une clôture ne se modifie ni ne se
               supprime ; il n'existe aucun déblocage. Clés étrangères vers l'envoi et l'instantané figés.
   Règles  : - chaque objet n'est créé que s'il n'existe pas ; aucune donnée n'est insérée ni modifiée ;
             - aucune instruction destructive, aucun secret.
   Production sans AutoMigrate : exécuter ce fichier une fois sur la base SoftTime, APRÈS 009
   (sqlcmd -d <base> -i 010_cloture_paie.sql).
   ===================================================================================== */

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.T_PAIE_CLOTURE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_PAIE_CLOTURE (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_T_PAIE_CLOTURE PRIMARY KEY,
        BDD_SAGE nvarchar(128) NOT NULL,
        PERIODE_DEBUT date NOT NULL,
        PERIODE_FIN date NOT NULL,
        CLOTURE_LE datetime2 NOT NULL,
        CLOTURE_PAR nvarchar(256) NOT NULL,
        ENVOI_ID int NOT NULL CONSTRAINT FK_T_PAIE_CLOTURE_ENVOI REFERENCES dbo.T_PAIE_ENVOI (ID),
        INSTANTANE_ID int NOT NULL CONSTRAINT FK_T_PAIE_CLOTURE_INSTANTANE REFERENCES dbo.T_PAIE_INSTANTANE (ID),
        EMPREINTE char(64) NOT NULL,
        STATUT_CONTROLE nvarchar(16) NOT NULL,
        RESUME_CONTROLE nvarchar(1000) NOT NULL,
        CONSTRAINT CK_T_PAIE_CLOTURE_PERIODE CHECK (PERIODE_DEBUT <= PERIODE_FIN)
    );
    PRINT N'010 : table T_PAIE_CLOTURE créée.';
END
ELSE
    PRINT N'010 : table T_PAIE_CLOTURE déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_PAIE_CLOTURE_BDD_PERIODE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_CLOTURE'))
    CREATE UNIQUE INDEX UX_T_PAIE_CLOTURE_BDD_PERIODE ON dbo.T_PAIE_CLOTURE (BDD_SAGE, PERIODE_DEBUT, PERIODE_FIN);
GO

IF OBJECT_ID(N'dbo.TR_T_PAIE_CLOTURE_IMMUABLE', N'TR') IS NULL
    EXEC (N'CREATE TRIGGER dbo.TR_T_PAIE_CLOTURE_IMMUABLE ON dbo.T_PAIE_CLOTURE INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51992, N''Clôture de paie définitive : modification et suppression interdites (T_PAIE_CLOTURE). Les corrections se font sur la période suivante.'', 1;
END');
GO
