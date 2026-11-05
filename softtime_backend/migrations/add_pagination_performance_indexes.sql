/* ============================================================================
   Pagination performance — base EXISTANTE.

   Objectif : transformer les filtres « base SAGE + plage de dates (+ matricule) »
   en recherches d'index, et rendre la pagination SQL (ORDER BY … OFFSET/FETCH)
   réellement indexée. Le script add_performance_indexes.sql ne créait que des
   index sur la date ; les index composites étaient bloqués par les colonnes en
   nvarchar(max), non indexables (erreur SQL 1919).

   Ce script :
     1. vérifie qu'aucune valeur ne serait tronquée ;
     2. borne les colonnes de filtre à nvarchar(128) / nvarchar(64) ;
     3. crée les index composites utilisés par les endpoints paginés.

   Idempotent : peut être rejoué sans risque. À exécuter dans une fenêtre de
   maintenance (les ALTER COLUMN prennent un verrou de schéma).

     sqlcmd -S "localhost\SQLEXPRESS" -E -C -d base_softtime_demo -i add_pagination_performance_indexes.sql

   Rollback : les colonnes peuvent être réélargies en nvarchar(max) ; les index
   restent valides. Faire une sauvegarde avant exécution.
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* Les tables d'archive sont créées au démarrage de l'application ; refuser clairement si ce
   n'est pas encore le cas, plutôt que d'échouer sur un « Invalid object name ». */
IF OBJECT_ID(N'dbo.T_HISTORIQUE_POINTAGE', N'U') IS NULL
   OR OBJECT_ID(N'dbo.T_HISTORIQUE_ANOMALIE', N'U') IS NULL
   OR OBJECT_ID(N'dbo.T_HISTORIQUE_HS', N'U') IS NULL
    THROW 51000, 'Tables d''archive absentes : démarrez l''application une première fois avant ce script.', 1;

/* ----------------------------------------------------------------------------
   1) Pré-contrôles : refuser avant toute modification si une valeur dépasse la
      longueur cible. Aucune troncature silencieuse.
   ---------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM dbo.T_POINTAGE WHERE LEN(NOM_BDD_SAGE) > 128)
    THROW 51000, 'T_POINTAGE.NOM_BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_POINTAGE WHERE LEN(MATRICULE_SAGE) > 64)
    THROW 51000, 'T_POINTAGE.MATRICULE_SAGE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_HEURECORRIGER WHERE LEN(BDD_SAGE) > 128)
    THROW 51000, 'T_HEURECORRIGER.BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_HEURECORRIGER WHERE LEN(MATRICULE_SAGE) > 64)
    THROW 51000, 'T_HEURECORRIGER.MATRICULE_SAGE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_HEUREANOMALIE WHERE LEN(BDD_SAGE) > 128)
    THROW 51000, 'T_HEUREANOMALIE.BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_HEUREANOMALIE WHERE LEN(MATRICULE_SAGE) > 64)
    THROW 51000, 'T_HEUREANOMALIE.MATRICULE_SAGE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_CANTINE WHERE LEN(NOM_BDD_SAGE) > 128)
    THROW 51000, 'T_CANTINE.NOM_BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_CANTINE WHERE LEN(MATRICULE_SAGE) > 64)
    THROW 51000, 'T_CANTINE.MATRICULE_SAGE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_FRAIS WHERE LEN(NOM_BDD_SAGE) > 128)
    THROW 51000, 'T_FRAIS.NOM_BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_FRAIS WHERE LEN(MATRICULE_SAGE) > 64)
    THROW 51000, 'T_FRAIS.MATRICULE_SAGE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_HISTORIQUE_POINTAGE WHERE LEN(NOM_BDD_SAGE) > 128)
    THROW 51000, 'T_HISTORIQUE_POINTAGE.NOM_BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_HISTORIQUE_POINTAGE WHERE LEN(MATRICULE_SAGE) > 64)
    THROW 51000, 'T_HISTORIQUE_POINTAGE.MATRICULE_SAGE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_HISTORIQUE_ANOMALIE WHERE LEN(BDD_SAGE) > 128)
    THROW 51000, 'T_HISTORIQUE_ANOMALIE.BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_HISTORIQUE_ANOMALIE WHERE LEN(MATRICULE_SAGE) > 64)
    THROW 51000, 'T_HISTORIQUE_ANOMALIE.MATRICULE_SAGE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_HISTORIQUE_HS WHERE LEN(BDD_SAGE) > 128)
    THROW 51000, 'T_HISTORIQUE_HS.BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_HISTORIQUE_HS WHERE LEN(MATRICULE) > 64)
    THROW 51000, 'T_HISTORIQUE_HS.MATRICULE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_PLANNING_SAL WHERE LEN(BDD_SAGE) > 128)
    THROW 51000, 'T_PLANNING_SAL.BDD_SAGE contient une valeur > 128 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_CARDPAIE WHERE LEN(SAGE_BDD) > 128)
    THROW 51000, 'T_CARDPAIE.SAGE_BDD contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_CARDPAIE WHERE LEN(SAGE_MATRICULE) > 64)
    THROW 51000, 'T_CARDPAIE.SAGE_MATRICULE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_HS_VALIDER WHERE LEN(BDD_SAGE) > 128)
    THROW 51000, 'T_HS_VALIDER.BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_HS_VALIDER WHERE LEN(MATRICULES) > 64)
    THROW 51000, 'T_HS_VALIDER.MATRICULES contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_HSExoImp WHERE LEN(BDD_SAGE) > 128)
    THROW 51000, 'T_HSExoImp.BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_HSExoImp WHERE LEN(Matricule) > 64)
    THROW 51000, 'T_HSExoImp.Matricule contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_HSSemaine WHERE LEN(BDD_SAGE) > 128)
    THROW 51000, 'T_HSSemaine.BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_HSSemaine WHERE LEN(Matricule) > 64)
    THROW 51000, 'T_HSSemaine.Matricule contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.T_HEUREDIMANCHE WHERE LEN(BDD_SAGE) > 128)
    THROW 51000, 'T_HEUREDIMANCHE.BDD_SAGE contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.T_HEUREDIMANCHE WHERE LEN(MATRICULE_SAGE) > 64)
    THROW 51000, 'T_HEUREDIMANCHE.MATRICULE_SAGE contient une valeur > 64 caractères.', 1;

IF EXISTS (SELECT 1 FROM dbo.suivi_SUIVI WHERE LEN([_TABLES]) > 128)
    THROW 51000, 'suivi_SUIVI._TABLES contient une valeur > 128 caractères.', 1;
IF EXISTS (SELECT 1 FROM dbo.suivi_SUIVI WHERE LEN([_ACTIONS]) > 128)
    THROW 51000, 'suivi_SUIVI._ACTIONS contient une valeur > 128 caractères.', 1;

/* ----------------------------------------------------------------------------
   2) Colonnes de filtre bornées (ALTER uniquement si la longueur diffère).
      max_length est en octets : nvarchar(128) => 256, nvarchar(64) => 128.
   ---------------------------------------------------------------------------- */
BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_POINTAGE') AND c.name = 'NOM_BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_POINTAGE ALTER COLUMN NOM_BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_POINTAGE') AND c.name = 'MATRICULE_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_POINTAGE ALTER COLUMN MATRICULE_SAGE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HEURECORRIGER') AND c.name = 'BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_HEURECORRIGER ALTER COLUMN BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HEURECORRIGER') AND c.name = 'MATRICULE_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_HEURECORRIGER ALTER COLUMN MATRICULE_SAGE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HEUREANOMALIE') AND c.name = 'BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_HEUREANOMALIE ALTER COLUMN BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HEUREANOMALIE') AND c.name = 'MATRICULE_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_HEUREANOMALIE ALTER COLUMN MATRICULE_SAGE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_CANTINE') AND c.name = 'NOM_BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_CANTINE ALTER COLUMN NOM_BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_CANTINE') AND c.name = 'MATRICULE_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_CANTINE ALTER COLUMN MATRICULE_SAGE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_FRAIS') AND c.name = 'NOM_BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_FRAIS ALTER COLUMN NOM_BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_FRAIS') AND c.name = 'MATRICULE_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_FRAIS ALTER COLUMN MATRICULE_SAGE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HISTORIQUE_POINTAGE') AND c.name = 'NOM_BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_HISTORIQUE_POINTAGE ALTER COLUMN NOM_BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HISTORIQUE_POINTAGE') AND c.name = 'MATRICULE_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_HISTORIQUE_POINTAGE ALTER COLUMN MATRICULE_SAGE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HISTORIQUE_ANOMALIE') AND c.name = 'BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_HISTORIQUE_ANOMALIE ALTER COLUMN BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HISTORIQUE_ANOMALIE') AND c.name = 'MATRICULE_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_HISTORIQUE_ANOMALIE ALTER COLUMN MATRICULE_SAGE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HISTORIQUE_HS') AND c.name = 'BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_HISTORIQUE_HS ALTER COLUMN BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HISTORIQUE_HS') AND c.name = 'MATRICULE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_HISTORIQUE_HS ALTER COLUMN MATRICULE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_PLANNING_SAL') AND c.name = 'BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_PLANNING_SAL ALTER COLUMN BDD_SAGE nvarchar(128) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_CARDPAIE') AND c.name = 'SAGE_BDD'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_CARDPAIE ALTER COLUMN SAGE_BDD nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_CARDPAIE') AND c.name = 'SAGE_MATRICULE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_CARDPAIE ALTER COLUMN SAGE_MATRICULE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HS_VALIDER') AND c.name = 'BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_HS_VALIDER ALTER COLUMN BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HS_VALIDER') AND c.name = 'MATRICULES'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_HS_VALIDER ALTER COLUMN MATRICULES nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HSExoImp') AND c.name = 'BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_HSExoImp ALTER COLUMN BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HSExoImp') AND c.name = 'Matricule'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_HSExoImp ALTER COLUMN Matricule nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HSSemaine') AND c.name = 'BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_HSSemaine ALTER COLUMN BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HSSemaine') AND c.name = 'Matricule'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_HSSemaine ALTER COLUMN Matricule nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HEUREDIMANCHE') AND c.name = 'BDD_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.T_HEUREDIMANCHE ALTER COLUMN BDD_SAGE nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.T_HEUREDIMANCHE') AND c.name = 'MATRICULE_SAGE'
             AND (t.name <> 'nvarchar' OR c.max_length <> 128))
    ALTER TABLE dbo.T_HEUREDIMANCHE ALTER COLUMN MATRICULE_SAGE nvarchar(64) NULL;

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.suivi_SUIVI') AND c.name = '_TABLES'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.suivi_SUIVI ALTER COLUMN [_TABLES] nvarchar(128) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.suivi_SUIVI') AND c.name = '_ACTIONS'
             AND (t.name <> 'nvarchar' OR c.max_length <> 256))
    ALTER TABLE dbo.suivi_SUIVI ALTER COLUMN [_ACTIONS] nvarchar(128) NULL;

/* ----------------------------------------------------------------------------
   3) Index composites de pagination.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_POINTAGE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_POINTAGE'))
    CREATE INDEX IX_T_POINTAGE_Pagination
        ON dbo.T_POINTAGE (NOM_BDD_SAGE, DATE_POINTAGE, HEURE_POINTAGE, MATRICULE_SAGE);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HEURECORRIGER_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HEURECORRIGER'))
    CREATE INDEX IX_T_HEURECORRIGER_Pagination
        ON dbo.T_HEURECORRIGER (BDD_SAGE, DATE_POINTAGE_IN, MATRICULE_SAGE);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HEUREANOMALIE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HEUREANOMALIE'))
    CREATE INDEX IX_T_HEUREANOMALIE_Pagination
        ON dbo.T_HEUREANOMALIE (BDD_SAGE, DATE_POINTAGE_IN, MATRICULE_SAGE);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HISTORIQUE_POINTAGE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HISTORIQUE_POINTAGE'))
    CREATE INDEX IX_T_HISTORIQUE_POINTAGE_Pagination
        ON dbo.T_HISTORIQUE_POINTAGE (NOM_BDD_SAGE, DATE_SUPPRESSION);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HISTORIQUE_ANOMALIE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HISTORIQUE_ANOMALIE'))
    CREATE INDEX IX_T_HISTORIQUE_ANOMALIE_Pagination
        ON dbo.T_HISTORIQUE_ANOMALIE (BDD_SAGE, DATE_SUPPRESSION);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HISTORIQUE_HS_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HISTORIQUE_HS'))
    CREATE INDEX IX_T_HISTORIQUE_HS_Pagination
        ON dbo.T_HISTORIQUE_HS (BDD_SAGE, DATE_SUPPRESSION);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_CANTINE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_CANTINE'))
    CREATE INDEX IX_T_CANTINE_Pagination
        ON dbo.T_CANTINE (NOM_BDD_SAGE, DATEDEB, DATEFIN, MATRICULE_SAGE);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_FRAIS_Pagination' AND object_id = OBJECT_ID(N'dbo.T_FRAIS'))
    CREATE INDEX IX_T_FRAIS_Pagination
        ON dbo.T_FRAIS (NOM_BDD_SAGE, DATEDEB, DATEFIN, MATRICULE_SAGE);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PLANNING_SAL_Pagination' AND object_id = OBJECT_ID(N'dbo.T_PLANNING_SAL'))
    CREATE INDEX IX_T_PLANNING_SAL_Pagination
        ON dbo.T_PLANNING_SAL (BDD_SAGE, DateP, IDSAL);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_CARDPAIE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_CARDPAIE'))
    CREATE INDEX IX_T_CARDPAIE_Pagination
        ON dbo.T_CARDPAIE (SAGE_BDD, SAGE_MATRICULE, ID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HS_VALIDER_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HS_VALIDER'))
    CREATE INDEX IX_T_HS_VALIDER_Pagination
        ON dbo.T_HS_VALIDER (BDD_SAGE, DATE_DEB, DATE_FIN, MATRICULES);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HSExoImp_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HSExoImp'))
    CREATE INDEX IX_T_HSExoImp_Pagination
        ON dbo.T_HSExoImp (BDD_SAGE, PeriodeDebut, PeriodeFin, Matricule);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HSSemaine_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HSSemaine'))
    CREATE INDEX IX_T_HSSemaine_Pagination
        ON dbo.T_HSSemaine (BDD_SAGE, PeriodeDebut, PeriodeFin, Matricule);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HEUREDIMANCHE_Pagination' AND object_id = OBJECT_ID(N'dbo.T_HEUREDIMANCHE'))
    CREATE INDEX IX_T_HEUREDIMANCHE_Pagination
        ON dbo.T_HEUREDIMANCHE (BDD_SAGE, Date_Dim, MATRICULE_SAGE);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_SUIVI_Date' AND object_id = OBJECT_ID(N'dbo.suivi_SUIVI'))
    CREATE INDEX IX_suivi_SUIVI_Date ON dbo.suivi_SUIVI ([_DATETIMES]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_suivi_SUIVI_Filtres' AND object_id = OBJECT_ID(N'dbo.suivi_SUIVI'))
    CREATE INDEX IX_suivi_SUIVI_Filtres
        ON dbo.suivi_SUIVI ([_TABLES], [_ACTIONS], [_DATETIMES]);

COMMIT TRANSACTION;

PRINT 'Index de pagination créés / vérifiés.';
