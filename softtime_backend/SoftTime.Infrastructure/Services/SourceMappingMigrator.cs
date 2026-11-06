using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class SourceMappingMigrator : ISourceMappingMigrator
{
    private readonly IUnitOfWork _uow;

    public SourceMappingMigrator(
        IUnitOfWork uow)
    {
        _uow = uow;
    }

    // Cree (idempotent) le modele de mapping dynamique et seed le catalogue des roles.
    public async Task EnsureMappingSchemaAsync(CancellationToken ct)
    {
        await _uow.ExecuteSqlAsync("""
            IF OBJECT_ID(N'dbo.T_FIELD_ROLE', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_FIELD_ROLE (
                    Code nvarchar(64) NOT NULL PRIMARY KEY,
                    Label nvarchar(128) NULL,
                    SystemType nvarchar(16) NOT NULL,
                    EntityKind nvarchar(32) NOT NULL,
                    IsRequired bit NOT NULL CONSTRAINT DF_T_FIELD_ROLE_IsRequired DEFAULT 0,
                    AutoMappingPatterns nvarchar(256) NULL,
                    SortOrder int NOT NULL CONSTRAINT DF_T_FIELD_ROLE_SortOrder DEFAULT 0
                );
            END

            IF OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_SOURCE_ENTITY_MAPPING (
                    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    SystemType nvarchar(16) NOT NULL,
                    SageDbId int NULL,
                    PointeuseDbId int NULL,
                    EntityKind nvarchar(32) NOT NULL,
                    SourceTable nvarchar(128) NULL
                );
            END

            IF OBJECT_ID(N'dbo.T_SOURCE_FIELD_MAPPING', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_SOURCE_FIELD_MAPPING (
                    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    EntityMappingId int NOT NULL,
                    FieldRoleCode nvarchar(64) NOT NULL,
                    SourceColumn nvarchar(128) NULL
                );
            END

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND has_filter = 0)
                DROP INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
                CREATE UNIQUE INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (SageDbId, EntityKind) WHERE SageDbId IS NOT NULL;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND has_filter = 0)
            -- Index uniques FILTRES : pour une ligne SAGE, PointeuseDbId est NULL (et inversement).
            -- Sans filtre, SQL Server considere tous les NULL comme egaux et n'autoriserait qu'UNE
            -- seule ligne par EntityKind pour tout le systeme (2e base Sage/pointeuse refusee).
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND filter_definition IS NULL)
                DROP INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Sage_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
                CREATE UNIQUE INDEX UX_SEM_Sage_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (SageDbId, EntityKind) WHERE SageDbId IS NOT NULL;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING') AND filter_definition IS NULL)
                DROP INDEX UX_SEM_Pte_Kind ON dbo.T_SOURCE_ENTITY_MAPPING;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SEM_Pte_Kind' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_ENTITY_MAPPING'))
                CREATE UNIQUE INDEX UX_SEM_Pte_Kind ON dbo.T_SOURCE_ENTITY_MAPPING (PointeuseDbId, EntityKind) WHERE PointeuseDbId IS NOT NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SFM_Entity_Role' AND object_id = OBJECT_ID(N'dbo.T_SOURCE_FIELD_MAPPING'))
                CREATE UNIQUE INDEX UX_SFM_Entity_Role ON dbo.T_SOURCE_FIELD_MAPPING (EntityMappingId, FieldRoleCode);

            INSERT INTO dbo.T_FIELD_ROLE (Code, Label, SystemType, EntityKind, IsRequired, AutoMappingPatterns, SortOrder)
            SELECT v.Code, v.Label, v.SystemType, v.EntityKind, v.IsRequired, v.AutoMappingPatterns, v.SortOrder
            FROM (VALUES
                (N'SAGE_MATRICULE', N'Matricule', N'SAGE', N'EMPLOYEE', 1, N'matricule|matric|mat', 10),
                (N'SAGE_EMPLOYEE_ID', N'Identifiant employé', N'SAGE', N'EMPLOYEE', 1, N'compteur|numsalarie|num_salarie|salarieid', 20),
                (N'SAGE_NOM', N'Nom', N'SAGE', N'EMPLOYEE', 0, N'nom|name', 30),
                (N'SAGE_PRENOM', N'Prénom', N'SAGE', N'EMPLOYEE', 0, N'prenom|prénom|firstname', 40),
                (N'SAGE_BADGE', N'Numéro de badge', N'SAGE', N'EMPLOYEE', 0, N'badge|badgenumber|numbadge|num_badge|numerobadge', 50),
                (N'SAGE_INACTIVE_FLAG', N'Indicateur désactivé', N'SAGE', N'EMPLOYEE', 0, N'desactive|désactivé|inactive|actif', 60),
                (N'SAGE_CIVILITE', N'Civilité', N'SAGE', N'EMPLOYEE', 0, N'civilite|civilité|sexe|genre', 70),
                (N'SAGE_AFF_EMPLOYEE_ID', N'Identifiant employé', N'SAGE', N'AFFECTATION', 1, N'numsalarie|num_salarie|compteur|salarieid', 10),
                (N'SAGE_AFF_DEPARTEMENT', N'Code département', N'SAGE', N'AFFECTATION', 0, N'departement|département|code_dep|dep', 20),
                (N'SAGE_AFF_SERVICE', N'Service', N'SAGE', N'AFFECTATION', 0, N'service', 30),
                (N'SAGE_AFF_START', N'Date début', N'SAGE', N'AFFECTATION', 0, N'datedebut|date_debut|debut|startdate', 40),
                (N'SAGE_AFF_END', N'Date sortie', N'SAGE', N'AFFECTATION', 0, N'datesortie|date_sortie|datefin|fin|enddate', 50),
                (N'SAGE_DEPT_CODE', N'Code département', N'SAGE', N'DEPARTMENT', 1, N'code|code_dep|code_departement', 10),
                (N'SAGE_DEPT_LABEL', N'Intitulé département', N'SAGE', N'DEPARTMENT', 0, N'intitule|intitulé|libelle|libellé|label', 20),
                (N'SAGE_CAL_DATE', N'Date', N'SAGE', N'COMPANY_CALENDAR', 1, N'periode|date|jour', 10),
                (N'SAGE_CAL_IS_HOLIDAY', N'Jour férié', N'SAGE', N'COMPANY_CALENDAR', 0, N'etat|etatjour|ferie|férié|holiday', 20),
                (N'SAGE_CST_CODE', N'Code constante', N'SAGE', N'CONSTANT', 1, N'code|codeconstante|code_constante', 10),
                (N'SAGE_CST_LABEL', N'Intitulé', N'SAGE', N'CONSTANT', 0, N'intitule|intitulé|libelle|libellé|label', 20),
                (N'SAGE_CST_OPERANDE', N'Opérande', N'SAGE', N'CONSTANT', 0, N'operande|codeoperande|ordre|noordre', 30),
                (N'SAGE_EVT_CODE', N'Code événement', N'SAGE', N'ABSENCE_EVENT', 0, N'code|codene|code_ne', 10),
                (N'SAGE_EVT_LABEL', N'Intitulé', N'SAGE', N'ABSENCE_EVENT', 0, N'intitule|intitulé|libelle|libellé|label', 20),
                (N'SAGE_EEV_EMPLOYEE_ID', N'Identifiant employé', N'SAGE', N'EMPLOYEE_EVENT', 1, N'numsalarie|num_salarie|compteur|salarieid', 10),
                (N'SAGE_EEV_CODE', N'Code événement', N'SAGE', N'EMPLOYEE_EVENT', 0, N'code|codene|code_ne', 20),
                (N'SAGE_EEV_START', N'Début période', N'SAGE', N'EMPLOYEE_EVENT', 0, N'periode|debut|start|datedebut', 30),
                (N'SAGE_EEV_END', N'Fin période', N'SAGE', N'EMPLOYEE_EVENT', 0, N'periode|fin|end|datefin', 40),
                (N'SAGE_EEV_MATIN', N'Matin', N'SAGE', N'EMPLOYEE_EVENT', 0, N'matin|am', 50),
                (N'SAGE_EEV_APRESMIDI', N'Après-midi', N'SAGE', N'EMPLOYEE_EVENT', 0, N'apresmidi|après-midi|pm', 60),
                (N'PTE_USER_ID', N'Identifiant utilisateur', N'POINTEUSE', N'PUNCH_USER', 1, N'userid|user_id|id', 10),
                (N'PTE_USER_BADGE', N'Badge', N'POINTEUSE', N'PUNCH_USER', 0, N'badge|badgenumber|badge_number', 20),
                (N'PTE_USER_SSN', N'SSN / matricule', N'POINTEUSE', N'PUNCH_USER', 0, N'ssn|matricule|matric', 30),
                (N'PTE_USER_NAME', N'Nom', N'POINTEUSE', N'PUNCH_USER', 0, N'name|nom', 40),
                (N'PTE_PUNCH_USER_ID', N'Identifiant utilisateur', N'POINTEUSE', N'PUNCH', 1, N'userid|user_id|id', 10),
                (N'PTE_PUNCH_DATETIME', N'Date/heure', N'POINTEUSE', N'PUNCH', 1, N'checktime|check_time|datetime|date', 20),
                (N'PTE_PUNCH_TYPE', N'Type (entrée/sortie)', N'POINTEUSE', N'PUNCH', 0, N'checktype|check_type|type|sens', 30)
            ) AS v(Code, Label, SystemType, EntityKind, IsRequired, AutoMappingPatterns, SortOrder)
            WHERE NOT EXISTS (SELECT 1 FROM dbo.T_FIELD_ROLE r WHERE r.Code = v.Code);
            """, ct);
    }

    // Convertit une fois les anciennes colonnes MAP_* en mappings (idempotent).
    public async Task MigrateLegacyMappingsAsync(CancellationToken ct)
    {
        var entRepo = _uow.Repository<SourceEntityMapping>();
        var fldRepo = _uow.Repository<SourceFieldMapping>();
        var existing = await entRepo.ListAsync(_ => true, ct);
        var sageDone = existing.Where(e => e.SageDbId != null).Select(e => e.SageDbId!.Value).ToHashSet();
        var pteDone = existing.Where(e => e.PointeuseDbId != null).Select(e => e.PointeuseDbId!.Value).ToHashSet();

        foreach (var row in await _uow.Repository<BddSage>().ListAsync(_ => true, ct))
        {
            if (sageDone.Contains(row.Id) || string.IsNullOrWhiteSpace(row.MapTable)) continue;
            var ent = new SourceEntityMapping { SystemType = "SAGE", SageDbId = row.Id, EntityKind = "EMPLOYEE", SourceTable = row.MapTable!.Trim() };
            await entRepo.AddAsync(ent, ct);
            await _uow.SaveChangesAsync(ct);
            await AddLegacyFieldsAsync(fldRepo, ent.Id, new[]
            {
                ("SAGE_MATRICULE", row.MapColMatricule),
                ("SAGE_NOM", row.MapColNom),
                ("SAGE_PRENOM", row.MapColPrenom),
                ("SAGE_BADGE", row.MapColBadge),
            }, ct);
        }

        foreach (var row in await _uow.Repository<BddPointeuse>().ListAsync(_ => true, ct))
        {
            if (pteDone.Contains(row.Id)) continue;
            if (!string.IsNullOrWhiteSpace(row.MapUserTable))
            {
                var ent = new SourceEntityMapping { SystemType = "POINTEUSE", PointeuseDbId = row.Id, EntityKind = "PUNCH_USER", SourceTable = row.MapUserTable!.Trim() };
                await entRepo.AddAsync(ent, ct);
                await _uow.SaveChangesAsync(ct);
                await AddLegacyFieldsAsync(fldRepo, ent.Id, new[]
                {
                    ("PTE_USER_ID", row.MapUserColId),
                    ("PTE_USER_BADGE", row.MapUserColBadge),
                    ("PTE_USER_SSN", row.MapUserColSsn),
                    ("PTE_USER_NAME", row.MapUserColNom),
                }, ct);
            }
            if (!string.IsNullOrWhiteSpace(row.MapPunchTable))
            {
                var ent = new SourceEntityMapping { SystemType = "POINTEUSE", PointeuseDbId = row.Id, EntityKind = "PUNCH", SourceTable = row.MapPunchTable!.Trim() };
                await entRepo.AddAsync(ent, ct);
                await _uow.SaveChangesAsync(ct);
                await AddLegacyFieldsAsync(fldRepo, ent.Id, new[]
                {
                    ("PTE_PUNCH_USER_ID", row.MapPunchColUserId),
                    ("PTE_PUNCH_DATETIME", row.MapPunchColDatetime),
                    ("PTE_PUNCH_TYPE", row.MapPunchColType),
                }, ct);
            }
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task AddLegacyFieldsAsync(IRepository<SourceFieldMapping> repo, int entityMappingId, (string Role, string? Column)[] pairs, CancellationToken ct)
    {
        foreach (var (role, column) in pairs)
        {
            if (string.IsNullOrWhiteSpace(column)) continue;
            await repo.AddAsync(new SourceFieldMapping
            {
                EntityMappingId = entityMappingId,
                FieldRoleCode = role,
                SourceColumn = column!.Trim()
            }, ct);
        }
    }
}
