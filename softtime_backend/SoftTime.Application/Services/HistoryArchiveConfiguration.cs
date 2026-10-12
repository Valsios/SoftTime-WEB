using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>
/// Tables d'archive des suppressions (ex-tables « Historique* » du desktop).
/// Le DDL est idempotent et exécuté au démarrage (voir <c>Program.cs</c>) : les entités sont
/// mappées par EF, les tables doivent donc exister avant toute requête.
/// </summary>
public static class HistoryArchiveConfiguration
{
    public static Task EnsureStorageAsync(IUnitOfWork uow, CancellationToken ct = default) => uow.ExecuteSqlAsync("""
        IF OBJECT_ID(N'dbo.T_HISTORIQUE_POINTAGE', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.T_HISTORIQUE_POINTAGE (
                ID                 int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                POINTAGE_ID        decimal(18,0) NULL,
                NOM_BDD_SAGE       nvarchar(max) NULL,
                NOM_BDD_POINTEUSE  nvarchar(max) NULL,
                MATRICULE_SAGE     nvarchar(max) NULL,
                DATE_POINTAGE      datetime2 NULL,
                HEURE_POINTAGE     time NULL,
                DATE_IMPORTATION   datetime2 NULL,
                IDUNIQUE_POINTAGE  decimal(18,2) NULL,
                TYPE_POINTAGE      nvarchar(max) NULL,
                DATE_SUPPRESSION   datetime2 NOT NULL
            );
            CREATE INDEX IX_HIST_POINTAGE_Date ON dbo.T_HISTORIQUE_POINTAGE (DATE_SUPPRESSION);
        END;

        IF OBJECT_ID(N'dbo.T_HISTORIQUE_ANOMALIE', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.T_HISTORIQUE_ANOMALIE (
                ID                 int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                ANOMALIE_ID        decimal(18,0) NULL,
                BDD_SAGE           nvarchar(max) NULL,
                BDD_POINTEUSE      nvarchar(max) NULL,
                MATRICULE_SAGE     nvarchar(max) NULL,
                DATE_POINTAGE_IN   datetime2 NULL,
                DATE_POINTAGE_OUT  datetime2 NULL,
                H1                 time NULL,
                H2                 time NULL,
                HENTREE            time NULL,
                HPS                time NULL,
                HPE                time NULL,
                HSORTIE            time NULL,
                ABS_AM             bit NULL,
                ABS_PM             bit NULL,
                FERIES             bit NULL,
                FERIES_AM          bit NULL,
                FERIES_PM          bit NULL,
                INTITULE_ABSENCE   nvarchar(max) NULL,
                DATE_SUPPRESSION   datetime2 NOT NULL
            );
            CREATE INDEX IX_HIST_ANOMALIE_Date ON dbo.T_HISTORIQUE_ANOMALIE (DATE_SUPPRESSION);
        END;

        IF OBJECT_ID(N'dbo.T_HISTORIQUE_HS', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.T_HISTORIQUE_HS (
                ID                int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                HS_ID             int NULL,
                MATRICULE         nvarchar(max) NULL,
                NUM_SAL           int NULL,
                EXO130            decimal(18,2) NULL,
                EXO150            decimal(18,2) NULL,
                I130              decimal(18,2) NULL,
                I150              decimal(18,2) NULL,
                PERIODE_DEBUT     datetime2 NULL,
                PERIODE_FIN       datetime2 NULL,
                FERIE             decimal(18,2) NULL,
                NUIT              decimal(18,2) NULL,
                DIM               decimal(18,2) NULL,
                RETARD            decimal(18,2) NULL,
                ABSENCE           decimal(18,2) NULL,
                BDD_SAGE          nvarchar(max) NULL,
                BDD_POINTEUSE     nvarchar(max) NULL,
                DATE_SUPPRESSION  datetime2 NOT NULL
            );
            CREATE INDEX IX_HIST_HS_Date ON dbo.T_HISTORIQUE_HS (DATE_SUPPRESSION);
        END;
        """, ct);
}
