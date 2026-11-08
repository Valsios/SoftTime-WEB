using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class PayrollWriteConfigurationService : IPayrollWriteConfigurationService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalDiscoveryService _discovery;

    public PayrollWriteConfigurationService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalDiscoveryService discovery)
    {
        _uow = uow;
        _tenant = tenant;
        _discovery = discovery;
    }

    // --- Configuration de l'ecriture de paie dynamique (bases AUTRE) ---

    public async Task<IReadOnlyList<PayrollWriteConfigurationDto>> ListPayrollWriteConfigurationsAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var list = await _uow.Repository<PaieEcriture>().ListReadOnlyAsync(c => c.BddSage == _tenant.SageDb, ct);
        return list.OrderBy(c => Array.IndexOf(PayrollWritingConfiguration.Categories, c.Categorie))
            .Select(c => new PayrollWriteConfigurationDto(c.Id, c.TableCible, c.ColMatricule, c.Categorie, c.ColValeur)).ToList();
    }

    public async Task<PayrollWriteConfigurationDto> SavePayrollWriteConfigurationAsync(PayrollWriteConfigurationDto dto, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var database = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(database.TypeBase, "AUTRE", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("La configuration d'écriture dynamique est réservée aux bases AUTRE.");
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var category = PayrollWritingConfiguration.NormalizeCategory(dto.Categorie);
        PayrollWritingConfiguration.ValidateIdentifier(dto.TableCible, "La table cible", allowQualifiedTable: true);
        PayrollWritingConfiguration.ValidateIdentifier(dto.ColMatricule, "La colonne matricule");
        PayrollWritingConfiguration.ValidateIdentifier(dto.ColValeur, "La colonne de valeur");
        var repo = _uow.Repository<PaieEcriture>();
        var current = await repo.ListAsync(c => c.BddSage == _tenant.SageDb, ct);
        if (current.Any(c => c.Id != dto.Id && string.Equals(c.Categorie, category, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"La catégorie {category} est déjà configurée pour cette base.");
        PaieEcriture entity;
        entity = await ChargerOuCreerAsync(dto, repo, ct);
        entity.TableCible = dto.TableCible!.Trim();
        entity.ColMatricule = dto.ColMatricule!.Trim();
        entity.Categorie = category;
        entity.ColValeur = dto.ColValeur!.Trim();
        await _uow.SaveChangesAsync(ct);
        return new PayrollWriteConfigurationDto(entity.Id, entity.TableCible, entity.ColMatricule, entity.Categorie, entity.ColValeur);
    }

    /// <summary>Configuration à enregistrer : nouvelle, ou existante appartenant à la base active.</summary>
    private async Task<PaieEcriture> ChargerOuCreerAsync(
        PayrollWriteConfigurationDto dto, IRepository<PaieEcriture> repo, CancellationToken ct)
    {
        PaieEcriture entity;
        if (dto.Id == 0)
        {
            entity = new PaieEcriture { BddSage = _tenant.SageDb };
            await repo.AddAsync(entity, ct);
        }
        else
        {
            entity = await repo.GetByIdAsync(dto.Id, ct) ?? throw new NotFoundException();
            if (!string.Equals(entity.BddSage, _tenant.SageDb, StringComparison.OrdinalIgnoreCase))
                throw new DomainException("Cette configuration appartient à une autre base.");
            repo.Update(entity);
        }
        return entity;
    }

    public async Task DeletePayrollWriteConfigurationAsync(int id, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var entity = await _uow.Repository<PaieEcriture>().GetByIdAsync(id, ct) ?? throw new NotFoundException();
        if (!string.Equals(entity.BddSage, _tenant.SageDb, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Cette configuration appartient à une autre base.");
        _uow.Repository<PaieEcriture>().Remove(entity);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ConnectionTestResultDto> TestPayrollWriteConfigurationAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var database = await _tenant.GetSageRowAsync(ct);
        if (!string.Equals(database.TypeBase, "AUTRE", StringComparison.OrdinalIgnoreCase))
            return new ConnectionTestResultDto(false, "Cette configuration est réservée aux bases AUTRE.");
        try
        {
            return await ValiderConfigurationAsync(database, ct);
        }
        catch (DomainException ex)
        {
            // Erreur SQL traduite ou règle de configuration : message lisible. Une erreur technique va au middleware.
            return new ConnectionTestResultDto(false, $"Validation impossible : {ex.Message}");
        }
    }

    /// <summary>Catégories toutes configurées sur une même table dbo existante, colonnes présentes.</summary>
    private async Task<ConnectionTestResultDto> ValiderConfigurationAsync(BddSage database, CancellationToken ct)
    {
        await PayrollWritingConfiguration.EnsureStorageAsync(_uow, ct);
        var configs = await _uow.Repository<PaieEcriture>().ListReadOnlyAsync(c => c.BddSage == _tenant.SageDb, ct);
        var missing = PayrollWritingConfiguration.Categories.FirstOrDefault(category => !configs.Any(c => string.Equals(c.Categorie, category, StringComparison.OrdinalIgnoreCase)));
        if (missing != null)
            return new ConnectionTestResultDto(false, $"La configuration d'écriture de la base AUTRE est incomplète : la catégorie {missing} n'est pas configurée.");
        var first = configs.First();
        if (configs.Any(c => !string.Equals(c.TableCible, first.TableCible, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(c.ColMatricule, first.ColMatricule, StringComparison.OrdinalIgnoreCase)))
            return new ConnectionTestResultDto(false, "Toutes les catégories doivent utiliser la même table cible et la même colonne matricule.");
        var (schema, table) = PayrollWritingConfiguration.SplitTable(first.TableCible);
        if (!string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase))
            return new ConnectionTestResultDto(false, "La découverte des tables ne prend actuellement en charge que le schéma dbo.");
        var tables = await _discovery.ListTablesAsync(SqlServerCredentials.Of(database), database.NomBd!, ct);
        if (!tables.Contains(table, StringComparer.OrdinalIgnoreCase))
            return new ConnectionTestResultDto(false, $"La table {first.TableCible} n'existe pas.");
        var columns = await _discovery.ListColumnsAsync(SqlServerCredentials.Of(database), database.NomBd!, table, ct);
        var required = configs.Select(c => c.ColValeur).Append(first.ColMatricule).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missingColumn = required.FirstOrDefault(column => !columns.Contains(column, StringComparer.OrdinalIgnoreCase));
        return missingColumn == null
            ? new ConnectionTestResultDto(true, "Configuration d'écriture valide.")
            : new ConnectionTestResultDto(false, $"La colonne {missingColumn} n'existe pas.");
    }
}
