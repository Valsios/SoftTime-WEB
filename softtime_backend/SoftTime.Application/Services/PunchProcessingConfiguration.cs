using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>
/// Stockage requis par le pré-traitement des pointages.
/// Le DDL est idempotent et exécuté au démarrage (voir <c>Program.cs</c>) : la colonne
/// <c>T_HEURECORRIGER.ORIGINE</c> est mappée par EF, elle doit donc exister avant toute requête.
/// </summary>
public static class PunchProcessingConfiguration
{
    public const string OrigineAuto = "AUTO";
    public const string OrigineManuel = "MANUEL";

    public static Task EnsureStorageAsync(IUnitOfWork uow, CancellationToken ct = default) => uow.ExecuteSqlAsync("""
        IF COL_LENGTH(N'dbo.T_HEURECORRIGER', N'ORIGINE') IS NULL
            ALTER TABLE dbo.T_HEURECORRIGER ADD ORIGINE nvarchar(10) NULL;

        IF COL_LENGTH(N'dbo.T_HEUREANOMALIE', N'MOTIF') IS NULL
            ALTER TABLE dbo.T_HEUREANOMALIE ADD MOTIF nvarchar(30) NULL;

        IF COL_LENGTH(N'dbo.T_CLOCK', N'SIMPLE_STRICT') IS NULL
            ALTER TABLE dbo.T_CLOCK ADD SIMPLE_STRICT bit NULL;
        IF COL_LENGTH(N'dbo.T_CLOCK', N'SIMPLE_HPSPE') IS NULL
            ALTER TABLE dbo.T_CLOCK ADD SIMPLE_HPSPE bit NULL;
        IF COL_LENGTH(N'dbo.T_CLOCK', N'DUREE_MIN_JOUR') IS NULL
            ALTER TABLE dbo.T_CLOCK ADD DUREE_MIN_JOUR int NULL;
        IF COL_LENGTH(N'dbo.T_CLOCK', N'DUREE_MAX_JOUR') IS NULL
            ALTER TABLE dbo.T_CLOCK ADD DUREE_MAX_JOUR int NULL;
        IF COL_LENGTH(N'dbo.T_CLOCK', N'FILTRE_POINTEUSE') IS NULL
            ALTER TABLE dbo.T_CLOCK ADD FILTRE_POINTEUSE bit NULL;
        IF COL_LENGTH(N'dbo.T_CLOCK', N'ANOMALIE_HORS_PLANNING') IS NULL
            ALTER TABLE dbo.T_CLOCK ADD ANOMALIE_HORS_PLANNING bit NULL;
        IF COL_LENGTH(N'dbo.T_CLOCK', N'SIMPLE_NUIT') IS NULL
            ALTER TABLE dbo.T_CLOCK ADD SIMPLE_NUIT bit NULL;
        """, ct);
}
