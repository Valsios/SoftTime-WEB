using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

public static class SageDatabaseConfiguration
{
    public static Task EnsureStorageAsync(IUnitOfWork uow, CancellationToken ct) => uow.ExecuteSqlAsync("""
        IF OBJECT_ID(N'dbo.T_BDD_SAGE', N'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_TABLE_CODE_CONSTANTE') IS NULL
                ALTER TABLE dbo.T_BDD_SAGE ADD MAP_TABLE_CODE_CONSTANTE nvarchar(128) NULL;
            IF COL_LENGTH(N'dbo.T_BDD_SAGE', N'MAP_COL_CODE_CONSTANTE') IS NULL
                ALTER TABLE dbo.T_BDD_SAGE ADD MAP_COL_CODE_CONSTANTE nvarchar(128) NULL;
        END;
        """, ct);
}
