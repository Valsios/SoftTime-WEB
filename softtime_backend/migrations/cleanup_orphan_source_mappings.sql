-- Nettoie les mappings dont la connexion parente (T_BDD_SAGE / T_BDD_POINTEUSE) n'existe plus.
-- Ces lignes orphelines bloquaient l'insertion de nouvelles bases a cause de l'index unique
-- (SageDbId/PointeuseDbId, EntityKind) : la FK non applicable est NULL et SQL Server traite
-- les NULL comme egaux dans un index unique.
--
-- Usage : sqlcmd -S "localhost\SQLEXPRESS" -E -C -d base_softtime -i migrations\cleanup_orphan_source_mappings.sql

SET NOCOUNT ON;
-- Options SET requises pour creer un index filtre (sqlcmd les met a OFF par defaut).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

-- Apercu (a commenter si besoin)
SELECT m.Id, m.SystemType, m.SageDbId, m.PointeuseDbId, m.EntityKind, m.SourceTable
FROM dbo.T_SOURCE_ENTITY_MAPPING AS m
LEFT JOIN dbo.T_BDD_SAGE AS s ON s.ID = m.SageDbId
LEFT JOIN dbo.T_BDD_POINTEUSE AS p ON p.ID = m.PointeuseDbId
WHERE (m.SageDbId IS NOT NULL AND s.ID IS NULL)
   OR (m.PointeuseDbId IS NOT NULL AND p.ID IS NULL);

-- Champs des mappings orphelins
DELETE f
FROM dbo.T_SOURCE_FIELD_MAPPING AS f
INNER JOIN dbo.T_SOURCE_ENTITY_MAPPING AS m ON m.Id = f.EntityMappingId
LEFT JOIN dbo.T_BDD_SAGE AS s ON s.ID = m.SageDbId
LEFT JOIN dbo.T_BDD_POINTEUSE AS p ON p.ID = m.PointeuseDbId
WHERE (m.SageDbId IS NOT NULL AND s.ID IS NULL)
   OR (m.PointeuseDbId IS NOT NULL AND p.ID IS NULL);

-- Mappings orphelins
DELETE m
FROM dbo.T_SOURCE_ENTITY_MAPPING AS m
LEFT JOIN dbo.T_BDD_SAGE AS s ON s.ID = m.SageDbId
LEFT JOIN dbo.T_BDD_POINTEUSE AS p ON p.ID = m.PointeuseDbId
WHERE (m.SageDbId IS NOT NULL AND s.ID IS NULL)
   OR (m.PointeuseDbId IS NOT NULL AND p.ID IS NULL);

-- Index uniques filtres (l'API les (re)cree aussi via EnsureMappingSchemaAsync)
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND filter_definition IS NULL)
    DROP INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
    CREATE UNIQUE INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (SageDbId, EntityKind) WHERE SageDbId IS NOT NULL;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND filter_definition IS NULL)
    DROP INDEX UX_SEM_Pte_Kind ON dbo.T_SOURCE_ENTITY_MAPPING;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
    CREATE UNIQUE INDEX UX_SEM_Pte_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (PointeuseDbId, EntityKind) WHERE PointeuseDbId IS NOT NULL;

PRINT 'Mappings orphelins nettoyes et index uniques filtres.';
