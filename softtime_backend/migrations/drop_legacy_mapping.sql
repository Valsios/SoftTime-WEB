/* ============================================================================
   SoftTime - Nettoyage du modele de mapping "legacy" (suppression physique)
   ----------------------------------------------------------------------------
   CONTEXTE
   Le mapping des sources de donnees se fait desormais via les tables
   T_FIELD_ROLE / T_SOURCE_ENTITY_MAPPING / T_SOURCE_FIELD_MAPPING.
   Le code applicatif (backend + frontend) n'utilise PLUS :
     - les colonnes MAP_* de T_BDD_SAGE et T_BDD_POINTEUSE
     - la table T_SOURCE_CONFIG

   CE SCRIPT (a executer MANUELLEMENT, une seule fois, quand vous etes pret) :
     ETAPE 1 - Recopie (backfill) les anciennes colonnes MAP_* vers les
               nouvelles tables de mapping. Idempotent : ne cree rien si un
               mapping existe deja pour la base concernee.
     ETAPE 2 - Supprime les colonnes MAP_* (DROP COLUMN).
     ETAPE 3 - Supprime la table T_SOURCE_CONFIG.

   *** A LIRE AVANT D'EXECUTER ***
   - Faites une sauvegarde de la base avant execution (ou au minimum un
     instantane), la suppression de colonnes est irreversible.
   - Executez le script avec un compte ayant les droits ALTER / DROP.
   - Le script est idempotent : vous pouvez le relancer sans erreur.
   - Pour NE FAIRE QUE la sauvegarde des donnees sans rien supprimer :
     executez uniquement l'ETAPE 1, puis commentez les etapes 2 et 3.

   LIMITE CONNUE DU BACKFILL
   L'ancienne configuration ne permettait de mapper que :
     - SAGE      : une table "employes" (matricule, nom, prenom, badge)
     - POINTEUSE : une table "utilisateurs" et une table "pointages"
   Le nouveau modele demande en plus un "Identifiant employe"
   (SAGE_EMPLOYEE_ID / SAGE_AFF_EMPLOYEE_ID) : cette information n'existait pas
   dans les anciennes colonnes. Apres ce script, ouvrez chaque base de type
   "Autre" dans l'ecran Bases SAGE / Bases pointeuse et completez les champs
   obligatoires signales par l'interface (sinon les employes ne seront pas lus).
   ============================================================================ */

SET NOCOUNT ON;
GO

PRINT '=== ETAPE 1/3 : backfill des colonnes MAP_* vers le modele de mapping ===';
GO

IF OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING', N'U') IS NULL
BEGIN
    PRINT 'ATTENTION : les tables de mapping (T_SOURCE_ENTITY_MAPPING) n''existent pas encore.';
    PRINT 'Lancez l''application une fois (ecran Bases SAGE / Bases pointeuse) puis relancez ce script.';
    PRINT 'Etape 1 ignoree.';
    RETURN;
END
GO

/* ---- 1.a  SAGE : entite EMPLOYEE ------------------------------------------ */
INSERT INTO dbo.T_SOURCE_ENTITY_MAPPING (SystemType, SageDbId, PointeuseDbId, EntityKind, SourceTable)
SELECT N'SAGE', s.ID, NULL, N'EMPLOYEE', LTRIM(RTRIM(s.MAP_TABLE))
FROM dbo.T_BDD_SAGE AS s
WHERE s.MAP_TABLE IS NOT NULL
  AND LTRIM(RTRIM(s.MAP_TABLE)) <> ''
  AND NOT EXISTS (
        SELECT 1 FROM dbo.T_SOURCE_ENTITY_MAPPING AS m
        WHERE m.SageDbId = s.ID AND m.EntityKind = N'EMPLOYEE');
GO

INSERT INTO dbo.T_SOURCE_FIELD_MAPPING (EntityMappingId, FieldRoleCode, SourceColumn)
SELECT m.Id, v.Role, LTRIM(RTRIM(v.Col))
FROM dbo.T_SOURCE_ENTITY_MAPPING AS m
INNER JOIN dbo.T_BDD_SAGE AS s ON s.ID = m.SageDbId
CROSS APPLY (VALUES
        (N'SAGE_MATRICULE', CAST(s.MAP_COL_MATRICULE AS nvarchar(128))),
        (N'SAGE_NOM',       CAST(s.MAP_COL_NOM       AS nvarchar(128))),
        (N'SAGE_PRENOM',    CAST(s.MAP_COL_PRENOM    AS nvarchar(128))),
        (N'SAGE_BADGE',     CAST(s.MAP_COL_BADGE     AS nvarchar(128)))
    ) AS v(Role, Col)
WHERE m.SystemType = N'SAGE'
  AND m.EntityKind = N'EMPLOYEE'
  AND v.Col IS NOT NULL
  AND LTRIM(RTRIM(v.Col)) <> ''
  AND NOT EXISTS (
        SELECT 1 FROM dbo.T_SOURCE_FIELD_MAPPING AS f
        WHERE f.EntityMappingId = m.Id AND f.FieldRoleCode = v.Role);
GO

/* ---- 1.b  POINTEUSE : entite PUNCH_USER ---------------------------------- */
INSERT INTO dbo.T_SOURCE_ENTITY_MAPPING (SystemType, SageDbId, PointeuseDbId, EntityKind, SourceTable)
SELECT N'POINTEUSE', NULL, p.ID, N'PUNCH_USER', LTRIM(RTRIM(p.MAP_USER_TABLE))
FROM dbo.T_BDD_POINTEUSE AS p
WHERE p.MAP_USER_TABLE IS NOT NULL
  AND LTRIM(RTRIM(p.MAP_USER_TABLE)) <> ''
  AND NOT EXISTS (
        SELECT 1 FROM dbo.T_SOURCE_ENTITY_MAPPING AS m
        WHERE m.PointeuseDbId = p.ID AND m.EntityKind = N'PUNCH_USER');
GO

INSERT INTO dbo.T_SOURCE_FIELD_MAPPING (EntityMappingId, FieldRoleCode, SourceColumn)
SELECT m.Id, v.Role, LTRIM(RTRIM(v.Col))
FROM dbo.T_SOURCE_ENTITY_MAPPING AS m
INNER JOIN dbo.T_BDD_POINTEUSE AS p ON p.ID = m.PointeuseDbId
CROSS APPLY (VALUES
        (N'PTE_USER_ID',    CAST(p.MAP_USER_COL_ID    AS nvarchar(128))),
        (N'PTE_USER_BADGE', CAST(p.MAP_USER_COL_BADGE AS nvarchar(128))),
        (N'PTE_USER_SSN',   CAST(p.MAP_USER_COL_SSN   AS nvarchar(128))),
        (N'PTE_USER_NAME',  CAST(p.MAP_USER_COL_NOM   AS nvarchar(128)))
    ) AS v(Role, Col)
WHERE m.SystemType = N'POINTEUSE'
  AND m.EntityKind = N'PUNCH_USER'
  AND v.Col IS NOT NULL
  AND LTRIM(RTRIM(v.Col)) <> ''
  AND NOT EXISTS (
        SELECT 1 FROM dbo.T_SOURCE_FIELD_MAPPING AS f
        WHERE f.EntityMappingId = m.Id AND f.FieldRoleCode = v.Role);
GO

/* ---- 1.c  POINTEUSE : entite PUNCH --------------------------------------- */
INSERT INTO dbo.T_SOURCE_ENTITY_MAPPING (SystemType, SageDbId, PointeuseDbId, EntityKind, SourceTable)
SELECT N'POINTEUSE', NULL, p.ID, N'PUNCH', LTRIM(RTRIM(p.MAP_PUNCH_TABLE))
FROM dbo.T_BDD_POINTEUSE AS p
WHERE p.MAP_PUNCH_TABLE IS NOT NULL
  AND LTRIM(RTRIM(p.MAP_PUNCH_TABLE)) <> ''
  AND NOT EXISTS (
        SELECT 1 FROM dbo.T_SOURCE_ENTITY_MAPPING AS m
        WHERE m.PointeuseDbId = p.ID AND m.EntityKind = N'PUNCH');
GO

INSERT INTO dbo.T_SOURCE_FIELD_MAPPING (EntityMappingId, FieldRoleCode, SourceColumn)
SELECT m.Id, v.Role, LTRIM(RTRIM(v.Col))
FROM dbo.T_SOURCE_ENTITY_MAPPING AS m
INNER JOIN dbo.T_BDD_POINTEUSE AS p ON p.ID = m.PointeuseDbId
CROSS APPLY (VALUES
        (N'PTE_PUNCH_USER_ID',  CAST(p.MAP_PUNCH_COL_USER_ID  AS nvarchar(128))),
        (N'PTE_PUNCH_DATETIME', CAST(p.MAP_PUNCH_COL_DATETIME AS nvarchar(128))),
        (N'PTE_PUNCH_TYPE',     CAST(p.MAP_PUNCH_COL_TYPE     AS nvarchar(128)))
    ) AS v(Role, Col)
WHERE m.SystemType = N'POINTEUSE'
  AND m.EntityKind = N'PUNCH'
  AND v.Col IS NOT NULL
  AND LTRIM(RTRIM(v.Col)) <> ''
  AND NOT EXISTS (
        SELECT 1 FROM dbo.T_SOURCE_FIELD_MAPPING AS f
        WHERE f.EntityMappingId = m.Id AND f.FieldRoleCode = v.Role);
GO

PRINT 'Etape 1 terminee.';
GO

/* ============================================================================
   ETAPE 2/3 : suppression des colonnes MAP_* (T_BDD_SAGE + T_BDD_POINTEUSE)
   ----------------------------------------------------------------------------
   Decommentez le bloc ci-dessous pour supprimer PHYSIQUEMENT les colonnes.
   ============================================================================ */

/*
PRINT '=== ETAPE 2/3 : suppression des colonnes MAP_* ===';
GO

IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_TABLE')               IS NOT NULL ALTER TABLE dbo.T_BDD_SAGE DROP COLUMN MAP_TABLE;
IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_COL_MATRICULE')       IS NOT NULL ALTER TABLE dbo.T_BDD_SAGE DROP COLUMN MAP_COL_MATRICULE;
IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_COL_NOM')             IS NOT NULL ALTER TABLE dbo.T_BDD_SAGE DROP COLUMN MAP_COL_NOM;
IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_COL_PRENOM')          IS NOT NULL ALTER TABLE dbo.T_BDD_SAGE DROP COLUMN MAP_COL_PRENOM;
IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_COL_BADGE')           IS NOT NULL ALTER TABLE dbo.T_BDD_SAGE DROP COLUMN MAP_COL_BADGE;
IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_COL_DEPARTEMENT')     IS NOT NULL ALTER TABLE dbo.T_BDD_SAGE DROP COLUMN MAP_COL_DEPARTEMENT;
IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_COL_SERVICE')         IS NOT NULL ALTER TABLE dbo.T_BDD_SAGE DROP COLUMN MAP_COL_SERVICE;
IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_COL_CODE_DEPARTEMENT') IS NOT NULL ALTER TABLE dbo.T_BDD_SAGE DROP COLUMN MAP_COL_CODE_DEPARTEMENT;
GO

IF COL_LENGTH(N'dbo.T_BDD_POINTEUSE', N'MAP_USER_TABLE')           IS NOT NULL ALTER TABLE dbo.T_BDD_POINTEUSE DROP COLUMN MAP_USER_TABLE;
IF COL_LENGTH(N'dbo.T_BDD_POINTEUSE', N'MAP_USER_COL_ID')          IS NOT NULL ALTER TABLE dbo.T_BDD_POINTEUSE DROP COLUMN MAP_USER_COL_ID;
IF COL_LENGTH(N'dbo.T_BDD_POINTEUSE', N'MAP_USER_COL_BADGE')       IS NOT NULL ALTER TABLE dbo.T_BDD_POINTEUSE DROP COLUMN MAP_USER_COL_BADGE;
IF COL_LENGTH(N'dbo.T_BDD_POINTEUSE', N'MAP_USER_COL_SSN')         IS NOT NULL ALTER TABLE dbo.T_BDD_POINTEUSE DROP COLUMN MAP_USER_COL_SSN;
IF COL_LENGTH(N'dbo.T_BDD_POINTEUSE', N'MAP_USER_COL_NOM')         IS NOT NULL ALTER TABLE dbo.T_BDD_POINTEUSE DROP COLUMN MAP_USER_COL_NOM;
IF COL_LENGTH(N'dbo.T_BDD_POINTEUSE', N'MAP_PUNCH_TABLE')          IS NOT NULL ALTER TABLE dbo.T_BDD_POINTEUSE DROP COLUMN MAP_PUNCH_TABLE;
IF COL_LENGTH(N'dbo.T_BDD_POINTEUSE', N'MAP_PUNCH_COL_USER_ID')    IS NOT NULL ALTER TABLE dbo.T_BDD_POINTEUSE DROP COLUMN MAP_PUNCH_COL_USER_ID;
IF COL_LENGTH(N'dbo.T_BDD_POINTEUSE', N'MAP_PUNCH_COL_DATETIME')   IS NOT NULL ALTER TABLE dbo.T_BDD_POINTEUSE DROP COLUMN MAP_PUNCH_COL_DATETIME;
IF COL_LENGTH(N'dbo.T_BDD_POINTEUSE', N'MAP_PUNCH_COL_TYPE')       IS NOT NULL ALTER TABLE dbo.T_BDD_POINTEUSE DROP COLUMN MAP_PUNCH_COL_TYPE;
GO
*/

/* ============================================================================
   ETAPE 3/3 : suppression de la table T_SOURCE_CONFIG
   ----------------------------------------------------------------------------
   Decommentez le bloc ci-dessous pour supprimer la table.
   ============================================================================ */

/*
PRINT '=== ETAPE 3/3 : suppression de T_SOURCE_CONFIG ===';
GO

IF OBJECT_ID(N'dbo.T_SOURCE_CONFIG', N'U') IS NOT NULL DROP TABLE dbo.T_SOURCE_CONFIG;
GO
*/
