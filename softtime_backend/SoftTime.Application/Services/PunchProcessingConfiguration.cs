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
        """, ct);
}
