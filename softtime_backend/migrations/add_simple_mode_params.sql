-- =============================================================================
-- Mode simple fiable : paramètres de pointage et motif d'anomalie.
-- Script idempotent, rejouable : ne modifie aucune donnée existante.
-- Miroir de PunchProcessingConfiguration.EnsureStorageAsync (appelé au démarrage).
-- =============================================================================

-- Motif d'anomalie (AUCUN_POINTAGE, POINTAGE_UNIQUE, POINTAGES_INCOHERENTS, DUREE, HORS_PLANNING).
IF COL_LENGTH(N'dbo.T_HEUREANOMALIE', N'MOTIF') IS NULL
    ALTER TABLE dbo.T_HEUREANOMALIE ADD MOTIF nvarchar(30) NULL;
GO

-- Paramètres globaux du mode simple (NULL = comportement historique).
IF COL_LENGTH(N'dbo.T_CLOCK', N'SIMPLE_STRICT') IS NULL
    ALTER TABLE dbo.T_CLOCK ADD SIMPLE_STRICT bit NULL;
GO
IF COL_LENGTH(N'dbo.T_CLOCK', N'SIMPLE_HPSPE') IS NULL
    ALTER TABLE dbo.T_CLOCK ADD SIMPLE_HPSPE bit NULL;
GO
IF COL_LENGTH(N'dbo.T_CLOCK', N'DUREE_MIN_JOUR') IS NULL
    ALTER TABLE dbo.T_CLOCK ADD DUREE_MIN_JOUR int NULL;
GO
IF COL_LENGTH(N'dbo.T_CLOCK', N'DUREE_MAX_JOUR') IS NULL
    ALTER TABLE dbo.T_CLOCK ADD DUREE_MAX_JOUR int NULL;
GO
IF COL_LENGTH(N'dbo.T_CLOCK', N'FILTRE_POINTEUSE') IS NULL
    ALTER TABLE dbo.T_CLOCK ADD FILTRE_POINTEUSE bit NULL;
GO
IF COL_LENGTH(N'dbo.T_CLOCK', N'ANOMALIE_HORS_PLANNING') IS NULL
    ALTER TABLE dbo.T_CLOCK ADD ANOMALIE_HORS_PLANNING bit NULL;
GO
IF COL_LENGTH(N'dbo.T_CLOCK', N'SIMPLE_NUIT') IS NULL
    ALTER TABLE dbo.T_CLOCK ADD SIMPLE_NUIT bit NULL;
GO
