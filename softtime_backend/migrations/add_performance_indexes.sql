/* ============================================================================
   Index de performance — base EXISTANTE.

   NOTE : ce script ne crée que des index sur les dates. Pour les index composites
   (base SAGE + date + matricule) et le bornage des colonnes nvarchar(max), voir
   migrations/add_pagination_performance_indexes.sql.

   Le filtre metier est toujours « base SAGE + plage de dates » (ou « base SAGE +
   matricule + plage de dates »). Sans index, chaque ecran scanne la table entiere :
   tableau de bord analytique, validation semaine, etats.

     Scope = requetes par salarie (val SAGE + matricule + dates)
     Date  = requetes par periode (tableau de bord, etats)

   Idempotent : peut etre rejoue sans risque, y compris apres une restauration.

     sqlcmd -S "localhost\SQLEXPRESS" -E -C -d base_softtime_demo -i add_performance_indexes.sql

   ATTENTION : BDD_SAGE / NOM_BDD_SAGE / MATRICULE_SAGE / MATRICULES / SAGE_BDD /
   SAGE_MATRICULE sont en nvarchar(max) (le modele EF ne declare aucune longueur) :
   elles ne peuvent PAS etre des colonnes cles d'index (erreur 1919). Les index
   « Scope » du bloc commente de create_softtime_database.sql sont donc inexploitables
   en l'etat — voir la section (2) en fin de fichier.

   On indexe ici les colonnes reellement indexables (datetime2 / int), ce qui suffit a
   transformer le balayage complet de la table en recherche de plage.
   ============================================================================ */

SET NOCOUNT ON;

-- 1) Index utilisables en l'etat.

-- Heures corrigees : plage de dates (tableau de bord, etats).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HEURECORRIGER_Date' AND object_id = OBJECT_ID(N'dbo.T_HEURECORRIGER'))
    CREATE INDEX IX_T_HEURECORRIGER_Date ON dbo.T_HEURECORRIGER (DATE_POINTAGE_IN);

-- Pointages : plage de dates.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_POINTAGE_Date' AND object_id = OBJECT_ID(N'dbo.T_POINTAGE'))
    CREATE INDEX IX_T_POINTAGE_Date ON dbo.T_POINTAGE (DATE_POINTAGE);

-- Anomalies : compteur du tableau de bord, par periode.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HEUREANOMALIE_Date' AND object_id = OBJECT_ID(N'dbo.T_HEUREANOMALIE'))
    CREATE INDEX IX_T_HEUREANOMALIE_Date ON dbo.T_HEUREANOMALIE (DATE_POINTAGE_IN);

-- Planning salarie : aucune n'existait. Plage de dates (tableau de bord) et salarie (validation semaine).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PLANNING_SAL_Date' AND object_id = OBJECT_ID(N'dbo.T_PLANNING_SAL'))
    CREATE INDEX IX_T_PLANNING_SAL_Date ON dbo.T_PLANNING_SAL (DateP);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_PLANNING_SAL_Salarie' AND object_id = OBJECT_ID(N'dbo.T_PLANNING_SAL'))
    CREATE INDEX IX_T_PLANNING_SAL_Salarie ON dbo.T_PLANNING_SAL (IDSAL, DateP);

-- Semaines validees : plage de dates (tableau de bord).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HS_VALIDER_Date' AND object_id = OBJECT_ID(N'dbo.T_HS_VALIDER'))
    CREATE INDEX IX_T_HS_VALIDER_Date ON dbo.T_HS_VALIDER (DATE_DEB);

-- Cumuls HS EXO/IMPO : par periode.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_T_HSExoImp_Periode' AND object_id = OBJECT_ID(N'dbo.T_HSExoImp'))
    CREATE INDEX IX_T_HSExoImp_Periode ON dbo.T_HSExoImp (PeriodeDebut, PeriodeFin);
GO

/* ----------------------------------------------------------------------------
   (2) OPTIONNEL — index par base SAGE + matricule (plus selectif que la date seule).

   Impossible aujourd'hui : ces colonnes sont en nvarchar(max). Executer d'abord les
   ALTER ci-dessous (elles echouent — sans rien modifier — si une valeur depasse la
   longueur indiquee), puis decommenter les CREATE INDEX.

   ALTER TABLE dbo.T_HEURECORRIGER ALTER COLUMN BDD_SAGE       nvarchar(128) NULL;
   ALTER TABLE dbo.T_HEURECORRIGER ALTER COLUMN MATRICULE_SAGE nvarchar(64)  NULL;
   ALTER TABLE dbo.T_HEUREANOMALIE ALTER COLUMN BDD_SAGE       nvarchar(128) NULL;
   ALTER TABLE dbo.T_HEUREANOMALIE ALTER COLUMN MATRICULE_SAGE nvarchar(64)  NULL;
   ALTER TABLE dbo.T_POINTAGE      ALTER COLUMN NOM_BDD_SAGE   nvarchar(128) NULL;
   ALTER TABLE dbo.T_POINTAGE      ALTER COLUMN MATRICULE_SAGE nvarchar(64)  NULL;
   ALTER TABLE dbo.T_PLANNING_SAL  ALTER COLUMN BDD_SAGE       nvarchar(128) NULL;
   ALTER TABLE dbo.T_PLANNING_SAL  ALTER COLUMN BDD_POINTEUSE  nvarchar(128) NULL;
   ALTER TABLE dbo.T_CARDPAIE      ALTER COLUMN SAGE_BDD       nvarchar(128) NULL;
   ALTER TABLE dbo.T_CARDPAIE      ALTER COLUMN SAGE_MATRICULE nvarchar(64)  NULL;
   ALTER TABLE dbo.T_HS_VALIDER    ALTER COLUMN BDD_SAGE       nvarchar(128) NULL;
   ALTER TABLE dbo.T_HS_VALIDER    ALTER COLUMN MATRICULES     nvarchar(64)  NULL;
   ALTER TABLE dbo.T_HSExoImp      ALTER COLUMN BDD_SAGE       nvarchar(128) NULL;

   CREATE INDEX IX_T_HEURECORRIGER_Scope ON dbo.T_HEURECORRIGER (BDD_SAGE, MATRICULE_SAGE, DATE_POINTAGE_IN);
   CREATE INDEX IX_T_PLANNING_SAL_Scope  ON dbo.T_PLANNING_SAL  (BDD_SAGE, IDSAL, DateP);
   CREATE INDEX IX_T_POINTAGE_Scope      ON dbo.T_POINTAGE      (NOM_BDD_SAGE, MATRICULE_SAGE, DATE_POINTAGE);
   CREATE INDEX IX_T_HEUREANOMALIE_Scope ON dbo.T_HEUREANOMALIE (BDD_SAGE, MATRICULE_SAGE, DATE_POINTAGE_IN);
   CREATE INDEX IX_T_CARDPAIE_Scope      ON dbo.T_CARDPAIE      (SAGE_BDD, SAGE_MATRICULE);
   CREATE INDEX IX_T_HS_VALIDER_Scope    ON dbo.T_HS_VALIDER    (BDD_SAGE, MATRICULES, DATE_DEB);
   CREATE INDEX IX_T_HSExoImp_Scope      ON dbo.T_HSExoImp      (BDD_SAGE, PeriodeDebut, PeriodeFin);
---------------------------------------------------------------------------- */
