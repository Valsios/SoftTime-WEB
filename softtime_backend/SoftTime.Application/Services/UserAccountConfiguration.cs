using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

/// <summary>
/// Stockage requis par les comptes utilisateurs.
/// Le DDL est idempotent et exécuté au démarrage (voir <c>Program.cs</c>) : la colonne
/// <c>T_RESPONSABLE.EMAIL</c> est mappée par EF, elle doit donc exister avant toute requête.
/// </summary>
public static class UserAccountConfiguration
{
    public static Task EnsureStorageAsync(IUnitOfWork uow, CancellationToken ct = default) => uow.ExecuteSqlAsync("""
        IF COL_LENGTH(N'dbo.T_RESPONSABLE', N'EMAIL') IS NULL
            ALTER TABLE dbo.T_RESPONSABLE ADD EMAIL nvarchar(200) NULL;
        """, ct);
}
