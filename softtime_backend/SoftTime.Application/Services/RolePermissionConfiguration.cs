using SoftTime.Domain.Repositories;

namespace SoftTime.Application.Services;

public static class RolePermissionConfiguration
{
    public static Task EnsureStorageAsync(IUnitOfWork uow, CancellationToken ct = default) => uow.ExecuteSqlAsync("""
        IF OBJECT_ID(N'dbo.T_ROLE_PERMISSION', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.T_ROLE_PERMISSION (
                ID              int           IDENTITY(1,1) NOT NULL,
                IDROLE          decimal(18,0) NOT NULL,
                PERMISSION_CODE nvarchar(128) NOT NULL,
                IS_GRANTED      bit           NOT NULL CONSTRAINT DF_T_ROLE_PERMISSION_IS_GRANTED DEFAULT (1),
                CONSTRAINT PK_T_ROLE_PERMISSION PRIMARY KEY (ID)
            );
            CREATE UNIQUE INDEX UX_T_ROLE_PERMISSION ON dbo.T_ROLE_PERMISSION (IDROLE, PERMISSION_CODE);
        END;

        IF OBJECT_ID(N'dbo.T_AUDIT_DROIT', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.T_AUDIT_DROIT (
                ID              int            IDENTITY(1,1) NOT NULL,
                DATE_ACTION     datetime2      NOT NULL DEFAULT SYSUTCDATETIME(),
                ID_OPERATEUR    decimal(18,0)  NOT NULL,
                LOGIN_OPERATEUR nvarchar(100)  NOT NULL,
                TYPE_CIBLE      nvarchar(20)   NOT NULL,
                ID_CIBLE        decimal(18,0)  NOT NULL,
                NOM_CIBLE       nvarchar(100)  NULL,
                ACTION          nvarchar(50)   NOT NULL,
                PERMS_AVANT     nvarchar(max)  NULL,
                PERMS_APRES     nvarchar(max)  NULL,
                CONSTRAINT PK_T_AUDIT_DROIT PRIMARY KEY (ID)
            );
            CREATE INDEX IX_T_AUDIT_DROIT_DATE   ON dbo.T_AUDIT_DROIT (DATE_ACTION DESC);
            CREATE INDEX IX_T_AUDIT_DROIT_CIBLE  ON dbo.T_AUDIT_DROIT (TYPE_CIBLE, ID_CIBLE);
        END;
        """, ct);
}
