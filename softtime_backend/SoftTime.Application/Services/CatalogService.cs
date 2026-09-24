using AutoMapper;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.Sage;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;
using SoftTime.Application.Abstractions;

namespace SoftTime.Application.Services;

public class CatalogService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly TenantConnectionService _tenant;
    private readonly ISageContextFactory _sageFactory;
    private readonly IPointeuseContextFactory _pointeuseFactory;
    private readonly IExternalSourceReader _externalReader;
    private readonly IExternalDiscoveryService _discovery;

    public CatalogService(IUnitOfWork uow, IMapper mapper, TenantConnectionService tenant, ISageContextFactory sageFactory, IPointeuseContextFactory pointeuseFactory, IExternalSourceReader externalReader, IExternalDiscoveryService discovery)
    {
        _uow = uow;
        _mapper = mapper;
        _tenant = tenant;
        _sageFactory = sageFactory;
        _pointeuseFactory = pointeuseFactory;
        _externalReader = externalReader;
        _discovery = discovery;
    }

    public Task<IReadOnlyList<SageDbDto>> ListSageAsync(CancellationToken ct = default)
        => MapList<T_BDD_SAGE, SageDbDto>(ct);

    public async Task<SageDbDto> SaveSageAsync(SageDbDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_BDD_SAGE>();
        T_BDD_SAGE entity;
        if (dto.Id == 0)
        {
            entity = _mapper.Map<T_BDD_SAGE>(dto);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.SERVEUR = dto.Serveur;
            entity.TLOGIN = dto.Login;
            entity.TMDP = dto.Password;
            entity.TYPE_AUTH = dto.SqlAuth;
            entity.NOM_BD = dto.NomBd;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<SageDbDto>(entity);
    }

    public async Task DeleteSageAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_BDD_SAGE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<PointeuseDbDto>> ListPointeuseAsync(CancellationToken ct = default)
        => MapList<T_BDD_POINTEUSE, PointeuseDbDto>(ct);

    public async Task<PointeuseDbDto> SavePointeuseAsync(PointeuseDbDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_BDD_POINTEUSE>();
        if (dto.Active == true)
        {
            var all = await repo.ListAsync(_ => true, ct);
            foreach (var p in all)
            {
                p.ACTIVE = p.ID == dto.Id;
                repo.Update(p);
            }
        }
        T_BDD_POINTEUSE entity;
        if (dto.Id == 0)
        {
            entity = _mapper.Map<T_BDD_POINTEUSE>(dto);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.SERVEUR = dto.Serveur;
            entity.TLOGIN = dto.Login;
            entity.TMDP = dto.Password;
            entity.TYPE_AUTH = dto.SqlAuth;
            entity.NOM_BD = dto.NomBd;
            entity.TYPE_POINTAGE = dto.TypePointage;
            entity.ACTIVE = dto.Active;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<PointeuseDbDto>(entity);
    }

    public async Task DeletePointeuseAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_BDD_POINTEUSE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ClockParamDto> GetClockAsync(CancellationToken ct = default)
    {
        var row = (await _uow.Repository<T_CLOCK>().ListAsync(_ => true, ct)).FirstOrDefault();
        return row == null ? new ClockParamDto(0, false) : new ClockParamDto(row.IDPOINT, row.MULTIPOINT);
    }

    public async Task<ClockParamDto> SaveClockAsync(ClockParamDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CLOCK>();
        var row = (await repo.ListAsync(_ => true, ct)).FirstOrDefault();
        if (row == null)
        {
            row = new T_CLOCK { MULTIPOINT = dto.MultiPoint };
            await repo.AddAsync(row, ct);
        }
        else
        {
            row.MULTIPOINT = dto.MultiPoint;
            repo.Update(row);
        }
        await _uow.SaveChangesAsync(ct);
        return new ClockParamDto(row.IDPOINT, row.MULTIPOINT);
    }

    public async Task<SourceConfigDto> GetSourceConfigAsync(CancellationToken ct = default)
    {
        await EnsureSourceConfigAsync(ct);
        var row = (await _uow.Repository<T_SOURCE_CONFIG>().ListAsync(_ => true, ct)).FirstOrDefault();
        return row == null
            ? new SourceConfigDto(0, "SAGE", null, null, null, null, null)
            : new SourceConfigDto(row.Id, row.Mode, row.TableName, row.ColMatricule, row.ColDepartement, row.ColService, row.ColCodeDepartement,
                row.ExtServeur, row.ExtBase, row.ExtLogin, row.ExtPassword, row.ExtSqlAuth);
    }

    public async Task<SourceConfigDto> SaveSourceConfigAsync(SourceConfigDto dto, CancellationToken ct = default)
    {
        await EnsureSourceConfigAsync(ct);
        if (dto.Mode != "SAGE" && dto.Mode != "AUTRE")
            throw new InvalidOperationException("Le mode doit être 'SAGE' ou 'AUTRE'.");
        if (dto.Mode == "AUTRE" && string.IsNullOrWhiteSpace(dto.TableName))
            throw new InvalidOperationException("Le nom de la table est requis en mode 'AUTRE'.");
        if (dto.Mode == "AUTRE" && !string.IsNullOrWhiteSpace(dto.ExtServeur) && string.IsNullOrWhiteSpace(dto.ExtBase))
            throw new InvalidOperationException("Le nom de la base est requis si un serveur externe est renseigné.");
        if (dto.Mode == "AUTRE" && !string.IsNullOrWhiteSpace(dto.ExtServeur) && dto.ExtSqlAuth && string.IsNullOrWhiteSpace(dto.ExtLogin))
            throw new InvalidOperationException("Le login est requis en authentification SQL.");

        var repo = _uow.Repository<T_SOURCE_CONFIG>();
        var row = (await repo.ListAsync(_ => true, ct)).FirstOrDefault();
        if (row == null)
        {
            row = new T_SOURCE_CONFIG
            {
                Mode = dto.Mode,
                TableName = dto.Mode == "AUTRE" ? dto.TableName : null,
                ColMatricule = dto.Mode == "AUTRE" ? dto.ColMatricule : null,
                ColDepartement = dto.Mode == "AUTRE" ? dto.ColDepartement : null,
                ColService = dto.Mode == "AUTRE" ? dto.ColService : null,
                ColCodeDepartement = dto.Mode == "AUTRE" ? dto.ColCodeDepartement : null,
                ExtServeur = dto.Mode == "AUTRE" ? dto.ExtServeur : null,
                ExtBase = dto.Mode == "AUTRE" ? dto.ExtBase : null,
                ExtLogin = dto.Mode == "AUTRE" ? dto.ExtLogin : null,
                ExtPassword = dto.Mode == "AUTRE" ? dto.ExtPassword : null,
                ExtSqlAuth = dto.Mode == "AUTRE" ? dto.ExtSqlAuth : true
            };
            await repo.AddAsync(row, ct);
        }
        else
        {
            row.Mode = dto.Mode;
            row.TableName = dto.Mode == "AUTRE" ? dto.TableName : null;
            row.ColMatricule = dto.Mode == "AUTRE" ? dto.ColMatricule : null;
            row.ColDepartement = dto.Mode == "AUTRE" ? dto.ColDepartement : null;
            row.ColService = dto.Mode == "AUTRE" ? dto.ColService : null;
            row.ColCodeDepartement = dto.Mode == "AUTRE" ? dto.ColCodeDepartement : null;
            row.ExtServeur = dto.Mode == "AUTRE" ? dto.ExtServeur : null;
            row.ExtBase = dto.Mode == "AUTRE" ? dto.ExtBase : null;
            row.ExtLogin = dto.Mode == "AUTRE" ? dto.ExtLogin : null;
            row.ExtPassword = dto.Mode == "AUTRE"
                ? (string.IsNullOrEmpty(dto.ExtPassword) ? row.ExtPassword : dto.ExtPassword)
                : null;
            row.ExtSqlAuth = dto.Mode == "AUTRE" ? dto.ExtSqlAuth : true;
            repo.Update(row);
        }
        await _uow.SaveChangesAsync(ct);
        return new SourceConfigDto(row.Id, row.Mode, row.TableName, row.ColMatricule, row.ColDepartement, row.ColService, row.ColCodeDepartement,
            row.ExtServeur, row.ExtBase, row.ExtLogin, row.ExtPassword, row.ExtSqlAuth);
    }

    public async Task<DepartementServiceDto> GetDepartementServiceAsync(string matricule, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(matricule))
            throw new InvalidOperationException("Le matricule est requis.");

        var config = await GetSourceConfigAsync(ct);
        return config.Mode == "AUTRE"
            ? await _externalReader.GetDepartementServiceAsync(await ResolveExternalConnectionAsync(config, ct), config, matricule, ct)
            : await LookupFromSageAsync(matricule, ct);
    }

    private async Task<string> ResolveExternalConnectionAsync(SourceConfigDto config, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(config.ExtServeur))
            return ExternalConnectionFactory.Build(config.ExtServeur, config.ExtBase, config.ExtSqlAuth, config.ExtLogin, config.ExtPassword);

        return await _tenant.GetSageConnectionAsync(ct);
    }

    private async Task<DepartementServiceDto> LookupFromSageAsync(string matricule, CancellationToken ct)
    {
        var mat = matricule.Trim();
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));

        var emp = sage.Employees.FirstOrDefault(e => e.MatriculeSalarie == mat);
        if (emp == null)
            return new DepartementServiceDto(matricule, null, null);

        var current = PickCurrentAffectation(sage.Affectations.Where(a => a.NumSalarie == emp.SA_CompteurNumero).ToList());

        return new DepartementServiceDto(matricule, current?.Departement?.Trim(), current?.Service?.Trim());
    }

    private static T_HST_AFFECTATION? PickCurrentAffectation(IEnumerable<T_HST_AFFECTATION> rows)
    {
        var list = rows.ToList();
        return list.Where(a => a.DateSortiePoste == null).OrderByDescending(a => a.DateDebut).FirstOrDefault()
               ?? list.OrderByDescending(a => a.DateDebut).FirstOrDefault();
    }

    public async Task<IReadOnlyList<DepartementServiceDto>> GetDepartementServiceBatchAsync(
    IEnumerable<string> matricules, CancellationToken ct = default)
    {
        var list = matricules
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct()
            .Take(2000)
            .ToList();
        if (list.Count == 0)
            return Array.Empty<DepartementServiceDto>();

        var config = await GetSourceConfigAsync(ct);

        if (config.Mode == "AUTRE")
        {
            var extConn = await ResolveExternalConnectionAsync(config, ct);
            var result = new List<DepartementServiceDto>();
            foreach (var m in list)
                result.Add(await _externalReader.GetDepartementServiceAsync(extConn, config, m, ct));
            return result;
        }

        var sageConn = await _tenant.GetSageConnectionAsync(ct);
        using var sage = _sageFactory.Create(sageConn);
        var matSet = list.ToHashSet();
        var emps = sage.Employees
            .ToList()
            .Where(e => matSet.Contains(e.MatriculeSalarie.Trim()))
            .Select(e => new { MatriculeSalarie = e.MatriculeSalarie.Trim(), e.SA_CompteurNumero })
            .ToList();
        var idSet = emps.Select(e => e.SA_CompteurNumero).ToHashSet();
        var byNum = sage.Affectations
            .ToList()
            .Where(a => idSet.Contains(a.NumSalarie))
            .GroupBy(a => a.NumSalarie)
            .ToDictionary(g => g.Key, g => g.ToList());

        var numByMat = emps.ToDictionary(e => e.MatriculeSalarie.Trim(), e => e.SA_CompteurNumero);
        return list.Select(m =>
        {
            var cur = numByMat.TryGetValue(m, out var num) && byNum.TryGetValue(num, out var rows)
                ? PickCurrentAffectation(rows) : null;
            return new DepartementServiceDto(m, cur?.Departement?.Trim(), cur?.Service?.Trim());
        }).ToList();
    }

    public Task<IReadOnlyList<string>> DiscoverServersAsync(CancellationToken ct = default)
        => _discovery.ListServersAsync(ct);

    public Task<IReadOnlyList<string>> DiscoverDatabasesAsync(DiscoverDatabasesDto dto, CancellationToken ct = default)
        => _discovery.ListDatabasesAsync(dto.Serveur, dto.SqlAuth, dto.Login, dto.Password, ct);

    public Task<IReadOnlyList<string>> DiscoverTablesAsync(DiscoverTablesDto dto, CancellationToken ct = default)
        => _discovery.ListTablesAsync(dto.Serveur, dto.Base, dto.SqlAuth, dto.Login, dto.Password, ct);

    public Task<IReadOnlyList<string>> DiscoverColumnsAsync(DiscoverColumnsDto dto, CancellationToken ct = default)
        => _discovery.ListColumnsAsync(dto.Serveur, dto.Base, dto.Table, dto.SqlAuth, dto.Login, dto.Password, ct);

    private async Task EnsureSourceConfigAsync(CancellationToken ct)
    {
        await _uow.ExecuteSqlAsync("""
            IF OBJECT_ID(N'dbo.T_SOURCE_CONFIG', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_SOURCE_CONFIG (
                    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    Mode nvarchar(10) NOT NULL DEFAULT 'SAGE',
                    TableName nvarchar(128) NULL,
                    ColMatricule nvarchar(128) NULL,
                    ColDepartement nvarchar(128) NULL,
                    ColService nvarchar(128) NULL,
                    ColCodeDepartement nvarchar(128) NULL,
                    ExtServeur nvarchar(256) NULL,
                    ExtBase nvarchar(128) NULL,
                    ExtLogin nvarchar(128) NULL,
                    ExtPassword nvarchar(max) NULL,
                    ExtSqlAuth bit NOT NULL DEFAULT 1
                );
            END
            ELSE
            BEGIN
                IF COL_LENGTH(N'dbo.T_SOURCE_CONFIG', N'ExtServeur') IS NULL
                    ALTER TABLE dbo.T_SOURCE_CONFIG ADD ExtServeur nvarchar(256) NULL;
                IF COL_LENGTH(N'dbo.T_SOURCE_CONFIG', N'ExtBase') IS NULL
                    ALTER TABLE dbo.T_SOURCE_CONFIG ADD ExtBase nvarchar(128) NULL;
                IF COL_LENGTH(N'dbo.T_SOURCE_CONFIG', N'ExtLogin') IS NULL
                    ALTER TABLE dbo.T_SOURCE_CONFIG ADD ExtLogin nvarchar(128) NULL;
                IF COL_LENGTH(N'dbo.T_SOURCE_CONFIG', N'ExtPassword') IS NULL
                    ALTER TABLE dbo.T_SOURCE_CONFIG ADD ExtPassword nvarchar(max) NULL;
                IF COL_LENGTH(N'dbo.T_SOURCE_CONFIG', N'ExtSqlAuth') IS NULL
                    ALTER TABLE dbo.T_SOURCE_CONFIG ADD ExtSqlAuth bit NOT NULL CONSTRAINT DF_T_SOURCE_CONFIG_ExtSqlAuth DEFAULT 1;
            END
            """, ct);
    }

    public async Task<CorrespondenceModeDto> GetCorrespondenceAsync(CancellationToken ct = default)
    {
        var row = (await _uow.Repository<T_ACTIVECORRESP>().ListAsync(_ => true, ct)).FirstOrDefault();
        return new CorrespondenceModeDto(row?.Active == true);
    }

    public async Task<CorrespondenceModeDto> SetCorrespondenceAsync(bool active, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_ACTIVECORRESP>();
        var row = (await repo.ListAsync(_ => true, ct)).FirstOrDefault();
        if (row == null)
        {
            row = new T_ACTIVECORRESP { Active = active };
            await repo.AddAsync(row, ct);
        }
        else
        {
            row.Active = active;
            repo.Update(row);
        }
        await _uow.SaveChangesAsync(ct);
        return new CorrespondenceModeDto(active);
    }

    public async Task<IReadOnlyList<CardPaieDto>> ListCardsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = (await _uow.Repository<T_CARDPAIE>().ListAsync(_ => true, ct))
            .Where(c =>
            {
                if (string.IsNullOrWhiteSpace(c.SAGE_BDD)) return true;
                return string.Equals(c.SAGE_BDD.Trim(), sage.Trim(), StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        return list.Select(_mapper.Map<CardPaieDto>).ToList();
    }

    public async Task<CardPaieDto> SaveCardAsync(CardPaieDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_CARDPAIE>();
        T_CARDPAIE entity;
        if (dto.Id == 0)
        {
            entity = _mapper.Map<T_CARDPAIE>(dto);
            entity.SAGE_BDD ??= _tenant.SageDb;
            entity.DATE ??= DateTime.Now;
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            _mapper.Map(dto, entity);
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<CardPaieDto>(entity);
    }

    public async Task DeleteCardAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CARDPAIE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<int> AutoMapCardsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageConn = await _tenant.GetSageConnectionAsync(ct);
        var pteConn = await _tenant.GetPointeuseConnectionAsync(ct);
        using var sage = _sageFactory.Create(sageConn);
        using var pte = _pointeuseFactory.Create(pteConn);
        var employees = sage.Employees.ToList();
        var badges = pte.Users.ToList();
        var repo = _uow.Repository<T_CARDPAIE>();
        var existing = await repo.ListAsync(c => c.SAGE_BDD == _tenant.SageDb, ct);
        var added = 0;
        foreach (var sal in employees.Where(e => e.SalarieDesactive != 1))
        {
            var match = badges.FirstOrDefault(b =>
                string.Equals(b.SSN?.Trim(), sal.MatriculeSalarie.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.BADGENUMBER?.Trim(), sal.MatriculeSalarie.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.BADGENUMBER?.Trim(), sal.NumeroDeBadge?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null)
                continue;
            if (existing.Any(c => c.SAGE_MATRICULE == sal.MatriculeSalarie))
                continue;
            await repo.AddAsync(new T_CARDPAIE
            {
                SAGE_MATRICULE = sal.MatriculeSalarie,
                SAGE_NOM = sal.Nom,
                SAGE_PRENOM = sal.Prenom,
                POINTEUSE_NUMERO = match.BADGENUMBER,
                POINTEUSE_NOM = match.NAME,
                SAGE_BDD = _tenant.SageDb,
                POINTEUSE_BDD = _tenant.PointeuseDb,
                DATE = DateTime.Now
            }, ct);
            added++;
        }
        await _uow.SaveChangesAsync(ct);
        return added;
    }

    public async Task<ImportResultDto> ImportCorrespondencesCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_CARDPAIE>();
        var existing = await repo.ListAsync(c => c.SAGE_BDD == _tenant.SageDb, ct);
        var imported = 0;
        var skipped = 0;
        foreach (var parts in lines)
        {
            if (parts.Length < 2)
            {
                skipped++;
                continue;
            }
            var badge = parts[0].Trim();
            var matricule = parts[1].Trim();
            var badgeFold = ExcelCells.Fold(badge);
            if (badgeFold is "BADGE" or "POINTEUSE" or "NPOINTEUSE" or "NUMERO")
            {
                skipped++;
                continue;
            }
            if (string.IsNullOrWhiteSpace(matricule)
                || ExcelCells.Fold(matricule) is "MATRICULE" or "MATRICULESAGE")
            {
                skipped++;
                continue;
            }
            var card = existing.FirstOrDefault(c => string.Equals(c.SAGE_MATRICULE, matricule, StringComparison.OrdinalIgnoreCase));
            if (card == null)
            {
                card = new T_CARDPAIE
                {
                    SAGE_MATRICULE = matricule,
                    POINTEUSE_NUMERO = badge,
                    SAGE_BDD = _tenant.SageDb,
                    POINTEUSE_BDD = _tenant.PointeuseDb,
                    DATE = DateTime.Now
                };
                await repo.AddAsync(card, ct);
                existing.Add(card);
            }
            else
            {
                card.POINTEUSE_NUMERO = badge;
                repo.Update(card);
            }
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped, "Correspondances importées.");
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == sage, ct);
        return list.Select(_mapper.Map<CategoryDto>).ToList();
    }

    public async Task<CategoryDto> SaveCategoryAsync(CategoryDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);

        var cardRepo = _uow.Repository<T_CARDPAIE>();
        var supervisorCard = await cardRepo.GetByIdAsync(dto.SupervisorCardId, ct);
        if (supervisorCard == null)
            throw new KeyNotFoundException($"Carte superviseur introuvable (ID={dto.SupervisorCardId}).");

        if (string.IsNullOrWhiteSpace(dto.Intitule))
            throw new InvalidOperationException("L'intitulé de la catégorie est obligatoire.");

        if (dto.HeuresSemaine <= 0)
            throw new InvalidOperationException("Le nombre d'heures par semaine doit être supérieur à 0.");

        var repo = _uow.Repository<T_CATEGORIE>();
        T_CATEGORIE entity;
        if (dto.Id == 0)
        {
            entity = new T_CATEGORIE
            {
                INTITULE = dto.Intitule,
                ID_CARDPAIE = dto.SupervisorCardId,
                PAUSE = dto.Pause,
                HEURESEMAINE = dto.HeuresSemaine,
                BDD_SAGE = _tenant.SageDb,
                BDD_POINTEUSE = _tenant.PointeuseDb
            };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.INTITULE = dto.Intitule;
            entity.ID_CARDPAIE = dto.SupervisorCardId;
            entity.PAUSE = dto.Pause;
            entity.HEURESEMAINE = dto.HeuresSemaine;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<CategoryDto>(entity);
    }

    public async Task DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CATEGORIE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AffectationDto>> ListAffectationsAsync(int? categoryId, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<T_CAT_SAL>().ListAsync(
            a => a.BDD_SAGE == sage && (!categoryId.HasValue || a.ID_CATEG == categoryId), ct);
        return list.Select(a => new AffectationDto(a.ID, a.MATRICULE_SAGE, a.ID_CATEG)).ToList();
    }

    public async Task<AffectationDto> SaveAffectationAsync(AffectationDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);

        var cardExists = await _uow.Repository<T_CARDPAIE>().GetByIdAsync(dto.CardPaieId, ct);
        if (cardExists == null)
            throw new KeyNotFoundException($"Carte salarié introuvable (ID={dto.CardPaieId}).");

        var categoryExists = await _uow.Repository<T_CATEGORIE>().GetByIdAsync(dto.CategoryId, ct);
        if (categoryExists == null)
            throw new KeyNotFoundException($"Catégorie introuvable (ID={dto.CategoryId}).");

        var repo = _uow.Repository<T_CAT_SAL>();
        T_CAT_SAL entity;

        if (dto.Id == 0)
        {
            var existingForCard = (await repo.ListAsync(a => a.MATRICULE_SAGE == dto.CardPaieId, ct)).FirstOrDefault();
            if (existingForCard != null)
            {
                existingForCard.ID_CATEG = dto.CategoryId;
                repo.Update(existingForCard);
                entity = existingForCard;
            }
            else
            {
                entity = new T_CAT_SAL
                {
                    MATRICULE_SAGE = dto.CardPaieId,
                    ID_CATEG = dto.CategoryId,
                    BDD_SAGE = _tenant.SageDb,
                    BDD_POINTEUSE = _tenant.PointeuseDb
                };
                await repo.AddAsync(entity, ct);
            }
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.MATRICULE_SAGE = dto.CardPaieId;
            entity.ID_CATEG = dto.CategoryId;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new AffectationDto(entity.ID, entity.MATRICULE_SAGE, entity.ID_CATEG);
    }

    public async Task<ImportResultDto> ImportAffectationsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var cats = await _uow.Repository<T_CATEGORIE>().ListAsync(c => c.BDD_SAGE == _tenant.SageDb, ct);
        var cards = await _uow.Repository<T_CARDPAIE>().ListAsync(c => c.SAGE_BDD == _tenant.SageDb, ct);
        var repo = _uow.Repository<T_CAT_SAL>();
        var existing = await repo.ListAsync(a => a.BDD_SAGE == _tenant.SageDb, ct);
        var imported = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            var catName = ExcelCells.Get(row, "Categorie", "CATEGORIE", "Intitule", "Intitulé")
                        ?? row.Values.FirstOrDefault();
            var mat = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE")
                    ?? (row.Count > 1 ? row.Values.Skip(1).FirstOrDefault() : null);
            if (string.IsNullOrWhiteSpace(catName) || string.IsNullOrWhiteSpace(mat))
            {
                skipped++;
                continue;
            }
            var cat = cats.FirstOrDefault(c => string.Equals(c.INTITULE?.Trim(), catName.Trim(), StringComparison.OrdinalIgnoreCase));
            var card = cards.FirstOrDefault(c => string.Equals(c.SAGE_MATRICULE?.Trim(), mat.Trim(), StringComparison.OrdinalIgnoreCase));
            if (cat == null || card == null)
            {
                skipped++;
                continue;
            }
            var found = existing.FirstOrDefault(a => a.MATRICULE_SAGE == card.ID);
            if (found == null)
            {
                found = new T_CAT_SAL
                {
                    MATRICULE_SAGE = card.ID,
                    ID_CATEG = cat.IDCATEGORIE,
                    BDD_SAGE = _tenant.SageDb,
                    BDD_POINTEUSE = _tenant.PointeuseDb
                };
                await repo.AddAsync(found, ct);
                existing.Add(found);
            }
            else
            {
                found.ID_CATEG = cat.IDCATEGORIE;
                repo.Update(found);
            }
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        return new ImportResultDto(imported, skipped, "Affectations importées.");
    }

    public async Task DeleteAffectationAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CAT_SAL>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ToleranceDto>> ListTolerancesAsync(int? categoryId, CancellationToken ct = default)
    {
        var list = await _uow.Repository<T_TOLERANCE>().ListAsync(
            t => !categoryId.HasValue || t.IDCATEGORIE == categoryId, ct);
        return list.Select(t => new ToleranceDto(t.ID, t.IDCATEGORIE, t.TOLERANCE, t.TYPES_TOL, t.SORTIE)).ToList();
    }

    public async Task<ToleranceDto> SaveToleranceAsync(ToleranceDto dto, CancellationToken ct = default)
    {
        var categoryExists = await _uow.Repository<T_CATEGORIE>().GetByIdAsync(dto.CategoryId, ct);
        if (categoryExists == null)
            throw new KeyNotFoundException($"Catégorie introuvable (ID={dto.CategoryId}).");

        if (dto.Tolerance < 0)
            throw new InvalidOperationException("La tolérance d'entrée ne peut pas être négative.");

        if (dto.Sortie < 0)
            throw new InvalidOperationException("La tolérance de sortie ne peut pas être négative.");

        var repo = _uow.Repository<T_TOLERANCE>();
        T_TOLERANCE entity;
        if (dto.Id == 0)
        {
            entity = new T_TOLERANCE
            {
                IDCATEGORIE = dto.CategoryId,
                TOLERANCE = dto.Tolerance,
                TYPES_TOL = dto.TypesTol,
                SORTIE = dto.Sortie
            };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.IDCATEGORIE = dto.CategoryId;
            entity.TOLERANCE = dto.Tolerance;
            entity.TYPES_TOL = dto.TypesTol;
            entity.SORTIE = dto.Sortie;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new ToleranceDto(entity.ID, entity.IDCATEGORIE, entity.TOLERANCE, entity.TYPES_TOL, entity.SORTIE);
    }

    public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sage = _tenant.SageDb;
        var list = await _uow.Repository<T_FERIE>().ListAsync(f => f.BDD_SAGE == sage, ct);
        return list.Select(_mapper.Map<HolidayDto>).ToList();
    }

    public async Task<HolidayDto> SaveHolidayAsync(HolidayDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_FERIE>();
        T_FERIE entity;
        if (dto.Id == 0)
        {
            var duplicate = dto.Date.HasValue
                ? (await repo.ListAsync(f => f.DATE.HasValue && f.DATE.Value.Date == dto.Date.Value.Date, ct)).FirstOrDefault()
                : null;
            if (duplicate != null)
            {
                duplicate.INTITULE = dto.Intitule;
                repo.Update(duplicate);
                entity = duplicate;
            }
            else
            {
                entity = new T_FERIE { INTITULE = dto.Intitule, DATE = dto.Date };
                await repo.AddAsync(entity, ct);
            }
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.INTITULE = dto.Intitule;
            entity.DATE = dto.Date;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<HolidayDto>(entity);
    }

    public async Task DeleteHolidayAsync(int id, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_FERIE>();
        var e = await repo.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        repo.Remove(e);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ImportSageHolidaysAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var cal = sage.CompanyCalendar.Where(c => c.EtatJour == 1).ToList();
        var repo = _uow.Repository<T_FERIE>();
        var existing = await repo.ListAsync(f => f.BDD_SAGE == _tenant.SageDb, ct);
        foreach (var day in cal.Where(d => d.PeriodeDebut.HasValue))
        {
            var date = day.PeriodeDebut!.Value.Date;
            if (existing.Any(e => e.DATE.HasValue && e.DATE.Value.Date == date))
                continue;
            await repo.AddAsync(new T_FERIE { DATE = date, INTITULE = "Férié SAGE", BDD_SAGE = _tenant.SageDb }, ct);
        }
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MajorationDto>> ListMajorationsAsync(CancellationToken ct = default)
    {
        // Desktop (RExtensions.getListMajoration) returns every row: the SAGE/pointeuse
        // filter is commented out, and existing data often has BDD_SAGE null.
        var list = await _uow.Repository<T_MAJORATION>().ListAsync(_ => true, ct);
        return list.OrderBy(m => m.Cotation).Select(_mapper.Map<MajorationDto>).ToList();
    }

    public async Task<MajorationDto> SaveMajorationAsync(MajorationDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_MAJORATION>();
        var entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
        entity.Cotation = dto.Cotation;
        repo.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<MajorationDto>(entity);
    }

    public async Task<IReadOnlyList<AbsenceCodeDto>> ListAbsenceCodesAsync(CancellationToken ct = default)
    {
        var list = await _uow.Repository<T_CODEABSENCE>().ListAsync(_ => true, ct);
        return list.Select(a => new AbsenceCodeDto(a.ID_ABSENCE, a.INTITULE_ABSENCE, a.NOT_PAY)).ToList();
    }

    public async Task<AbsenceCodeDto> SaveAbsenceCodeAsync(AbsenceCodeDto dto, CancellationToken ct = default)
    {
        var repo = _uow.Repository<T_CODEABSENCE>();
        T_CODEABSENCE entity;
        if (dto.Id == 0)
        {
            entity = new T_CODEABSENCE { INTITULE_ABSENCE = dto.Intitule, NOT_PAY = dto.NotPay };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
            entity.INTITULE_ABSENCE = dto.Intitule;
            entity.NOT_PAY = dto.NotPay;
            repo.Update(entity);
        }
        await _uow.SaveChangesAsync(ct);
        return new AbsenceCodeDto(entity.ID_ABSENCE, entity.INTITULE_ABSENCE, entity.NOT_PAY);
    }

    public async Task<IReadOnlyList<CodeConstanteDto>> ListCodeConstantesAsync(CancellationToken ct = default)
    {
        await EnsureCodeConstantesAsync(ct);
        var list = await _uow.Repository<T_CODE_CONSTANTE>().ListAsync(_ => true, ct);
        var order = OvertimeCodeKeys.Defaults.Select((d, i) => (d.Key, i)).ToDictionary(x => x.Key, x => x.i);
        return list
            .OrderBy(c => order.TryGetValue(c.CATEGORIE, out var i) ? i : 99)
            .Select(_mapper.Map<CodeConstanteDto>)
            .ToList();
    }

    public async Task<CodeConstanteDto> SaveCodeConstanteAsync(CodeConstanteDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var repo = _uow.Repository<T_CODE_CONSTANTE>();
        var entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new KeyNotFoundException();
        entity.CODE_CONSTANTE = (dto.CodeConstante ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(entity.CODE_CONSTANTE))
            throw new InvalidOperationException("Le code constante SAGE est requis.");
        repo.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return _mapper.Map<CodeConstanteDto>(entity);
    }

    public async Task<IReadOnlyList<SageConstantOptionDto>> ListSageConstantOptionsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        return sage.Constants.ToArray()
            .GroupBy(c => (c.CodeConstante ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .OrderBy(g => g.Key)
            .Select(g => new SageConstantOptionDto(g.Key, g.First().Intitule))
            .ToList();
    }

    public async Task<OvertimeSageCodes> GetOvertimeSageCodesAsync(CancellationToken ct = default)
    {
        await EnsureCodeConstantesAsync(ct);
        var list = await _uow.Repository<T_CODE_CONSTANTE>().ListAsync(_ => true, ct);
        string Code(string key, string fallback) =>
            list.FirstOrDefault(c => c.CATEGORIE == key)?.CODE_CONSTANTE is { Length: > 0 } v ? v.Trim() : fallback;

        return new OvertimeSageCodes(
            Code(OvertimeCodeKeys.Exo130, "HS01"),
            Code(OvertimeCodeKeys.Exo150, "HS02"),
            Code(OvertimeCodeKeys.I130, "HS03"),
            Code(OvertimeCodeKeys.I150, "HS04"),
            Code(OvertimeCodeKeys.Ferie, "HS05"),
            Code(OvertimeCodeKeys.Dim, "HS06"),
            Code(OvertimeCodeKeys.Nuit, "HS07"));
    }

    private async Task EnsureCodeConstantesAsync(CancellationToken ct)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        await _uow.ExecuteSqlAsync("""
            IF OBJECT_ID(N'dbo.T_CODE_CONSTANTE', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_CODE_CONSTANTE (
                    ID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    CATEGORIE nvarchar(32) NOT NULL,
                    INTITULE nvarchar(64) NULL,
                    CODE_CONSTANTE nvarchar(20) NOT NULL
                );
            END
            ELSE IF COL_LENGTH(N'dbo.T_CODE_CONSTANTE', N'BDD_SAGE') IS NOT NULL
            BEGIN
                DELETE FROM dbo.T_CODE_CONSTANTE
                WHERE ID NOT IN (SELECT MIN(ID) FROM dbo.T_CODE_CONSTANTE GROUP BY CATEGORIE);
                ALTER TABLE dbo.T_CODE_CONSTANTE DROP COLUMN BDD_SAGE;
            END
            """, ct);

        var repo = _uow.Repository<T_CODE_CONSTANTE>();
        var existing = await repo.ListAsync(_ => true, ct);
        var added = false;
        foreach (var (key, intitule, defaultCode) in OvertimeCodeKeys.Defaults)
        {
            if (existing.Any(c => c.CATEGORIE == key))
                continue;
            await repo.AddAsync(new T_CODE_CONSTANTE
            {
                CATEGORIE = key,
                INTITULE = intitule,
                CODE_CONSTANTE = defaultCode,
            }, ct);
            added = true;
        }
        if (added)
            await _uow.SaveChangesAsync(ct);
    }

    public async Task SyncAbsenceCodesFromSageAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        using var sage = _sageFactory.Create(await _tenant.GetSageConnectionAsync(ct));
        var events = sage.Events.ToList();
        var repo = _uow.Repository<T_CODEABSENCE>();
        var existing = await repo.ListAsync(_ => true, ct);
        foreach (var ev in events)
        {
            if (existing.Any(e => e.INTITULE_ABSENCE == ev.Intitule || e.INTITULE_ABSENCE == ev.CodeNE))
                continue;
            var newCode = new T_CODEABSENCE { INTITULE_ABSENCE = ev.Intitule ?? ev.CodeNE, NOT_PAY = false };
            await repo.AddAsync(newCode, ct);
            existing.Add(newCode);
        }
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<TDto>> MapList<TEntity, TDto>(CancellationToken ct) where TEntity : class
    {
        var list = await _uow.Repository<TEntity>().ListAsync(_ => true, ct);
        return list.Select(e => _mapper.Map<TDto>(e)).ToList();
    }
}