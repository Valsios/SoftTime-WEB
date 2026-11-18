/* =====================================================================================
   SoftTime — Migration 011 : domaines des envois en paie (heures, cantine, frais) — REJOUABLE
   -------------------------------------------------------------------------------------
   Cible   : SQL Server 2016+ (base SoftTime, schéma dbo), base courante, aucun USE.
   Objet   : - dbo.T_PAIE_ENVOI.DOMAINE et dbo.T_PAIE_INSTANTANE.DOMAINE : domaine de l'envoi et de son instantané,
               « HEURES » (variables d'heures, seul domaine avant ce script), « CANTINE » ou « FRAIS » (cumuls cantine et
               frais de déplacement de T_CUMSAL). Colonne NOT NULL avec la valeur par défaut « HEURES » : les lignes
               existantes et les écritures des versions précédentes de l'application restent des envois d'heures.
               Contrainte CHECK sur les trois valeurs. Index (base, domaine, période) des envois.
             - dbo.T_PAIE_CLOTURE_DOMAINE : pour une clôture (T_PAIE_CLOTURE), l'envoi, l'instantané, la version du format
               et l'empreinte figés de chaque domaine CANTINE ou FRAIS exigé au moment de la clôture. Les heures restent
               dans T_PAIE_CLOTURE (inchangée). Unicité (clôture, domaine).
             - Immuabilité : déclencheur INSTEAD OF UPDATE, DELETE sur T_PAIE_CLOTURE_DOMAINE (erreur 51992), comme
               T_PAIE_CLOTURE ; les déclencheurs de la migration 009 (T_PAIE_INSTANTANE, T_PAIE_INSTANTANE_LIGNE) couvrent
               déjà les instantanés des nouveaux domaines. Clés étrangères vers la clôture, l'envoi et l'instantané.
             - L'activation d'un domaine pour une base est une option de T_PAIE_OPTION (migration 008, clés
               « DomaineCantineActif » et « DomaineFraisActif ») : aucun objet à créer, une base sans ligne vaut « désactivé ».
   Règles  : - chaque objet n'est créé que s'il n'existe pas ; aucune donnée n'est insérée ni modifiée (les lignes
               existantes reçoivent seulement la valeur par défaut de la nouvelle colonne) ;
             - aucune instruction destructive, aucun secret ;
             - les envois de cantine et de frais antérieurs à ce script n'ont ni marqueur ni instantané (aucune reprise).
   Production sans AutoMigrate : exécuter ce fichier une fois sur la base SoftTime, APRÈS 009 et 010
   (sqlcmd -d <base> -i 011_domaines_paie.sql).
   ===================================================================================== */

SET NOCOUNT ON;
GO

IF COL_LENGTH(N'dbo.T_PAIE_ENVOI', N'DOMAINE') IS NULL
BEGIN
    ALTER TABLE dbo.T_PAIE_ENVOI ADD DOMAINE nvarchar(16) NOT NULL CONSTRAINT DF_T_PAIE_ENVOI_DOMAINE DEFAULT (N'HEURES');
    PRINT N'011 : colonne T_PAIE_ENVOI.DOMAINE créée.';
END
ELSE
    PRINT N'011 : colonne T_PAIE_ENVOI.DOMAINE déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_T_PAIE_ENVOI_DOMAINE' AND parent_object_id = OBJECT_ID(N'dbo.T_PAIE_ENVOI'))
    ALTER TABLE dbo.T_PAIE_ENVOI ADD CONSTRAINT CK_T_PAIE_ENVOI_DOMAINE CHECK (DOMAINE IN (N'HEURES', N'CANTINE', N'FRAIS'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PAIE_ENVOI_BDD_DOMAINE_PERIODE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_ENVOI'))
    CREATE INDEX IX_T_PAIE_ENVOI_BDD_DOMAINE_PERIODE ON dbo.T_PAIE_ENVOI (BDD_SAGE, DOMAINE, PERIODE_DEBUT, PERIODE_FIN);
GO

IF COL_LENGTH(N'dbo.T_PAIE_INSTANTANE', N'DOMAINE') IS NULL
BEGIN
    ALTER TABLE dbo.T_PAIE_INSTANTANE ADD DOMAINE nvarchar(16) NOT NULL CONSTRAINT DF_T_PAIE_INSTANTANE_DOMAINE DEFAULT (N'HEURES');
    PRINT N'011 : colonne T_PAIE_INSTANTANE.DOMAINE créée.';
END
ELSE
    PRINT N'011 : colonne T_PAIE_INSTANTANE.DOMAINE déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_T_PAIE_INSTANTANE_DOMAINE' AND parent_object_id = OBJECT_ID(N'dbo.T_PAIE_INSTANTANE'))
    ALTER TABLE dbo.T_PAIE_INSTANTANE ADD CONSTRAINT CK_T_PAIE_INSTANTANE_DOMAINE CHECK (DOMAINE IN (N'HEURES', N'CANTINE', N'FRAIS'));
GO

IF OBJECT_ID(N'dbo.T_PAIE_CLOTURE_DOMAINE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.T_PAIE_CLOTURE_DOMAINE (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_T_PAIE_CLOTURE_DOMAINE PRIMARY KEY,
        CLOTURE_ID int NOT NULL CONSTRAINT FK_T_PAIE_CLOTURE_DOMAINE_CLOTURE REFERENCES dbo.T_PAIE_CLOTURE (ID),
        DOMAINE nvarchar(16) NOT NULL,
        ENVOI_ID int NOT NULL CONSTRAINT FK_T_PAIE_CLOTURE_DOMAINE_ENVOI REFERENCES dbo.T_PAIE_ENVOI (ID),
        INSTANTANE_ID int NOT NULL CONSTRAINT FK_T_PAIE_CLOTURE_DOMAINE_INSTANTANE REFERENCES dbo.T_PAIE_INSTANTANE (ID),
        VERSION_FORMAT int NOT NULL,
        EMPREINTE char(64) NOT NULL,
        CONSTRAINT CK_T_PAIE_CLOTURE_DOMAINE_DOMAINE CHECK (DOMAINE IN (N'CANTINE', N'FRAIS'))
    );
    PRINT N'011 : table T_PAIE_CLOTURE_DOMAINE créée.';
END
ELSE
    PRINT N'011 : table T_PAIE_CLOTURE_DOMAINE déjà présente.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_T_PAIE_CLOTURE_DOMAINE' AND object_id = OBJECT_ID(N'dbo.T_PAIE_CLOTURE_DOMAINE'))
    CREATE UNIQUE INDEX UX_T_PAIE_CLOTURE_DOMAINE ON dbo.T_PAIE_CLOTURE_DOMAINE (CLOTURE_ID, DOMAINE);
GO

IF OBJECT_ID(N'dbo.TR_T_PAIE_CLOTURE_DOMAINE_IMMUABLE', N'TR') IS NULL
    EXEC (N'CREATE TRIGGER dbo.TR_T_PAIE_CLOTURE_DOMAINE_IMMUABLE ON dbo.T_PAIE_CLOTURE_DOMAINE INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51992, N''Clôture de paie définitive : modification et suppression interdites (T_PAIE_CLOTURE_DOMAINE). Les corrections se font sur la période suivante.'', 1;
END');
GO
