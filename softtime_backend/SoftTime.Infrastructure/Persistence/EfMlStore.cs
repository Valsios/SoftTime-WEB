using System.Data;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;

using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Infrastructure.Persistence;

public sealed class EfMlStore : IMlStore
{
    private readonly MlDbContext _db;

    public EfMlStore(
        MlDbContext db)
    {
        _db = db;
    }

    private const int BatchSize = 2000;

    public Task EnsureSchemaAsync(CancellationToken ct = default) => _db.Database.MigrateAsync(ct);

    public async Task ReplaceDailyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<MlSalarieJour> rows, CancellationToken ct = default)
    {
        var table = MlBulkTables.BuildDailyTable(rows);
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await DeleteInChunksAsync("SalarieJour", "Date", bddSage, from, to, ct);
            await BulkInsertAsync(table, "SalarieJour", transaction, ct);
            await transaction.CommitAsync(ct);
        });
    }

    public async Task ReplaceWeeklyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<MlSalarieSemaine> rows, CancellationToken ct = default)
    {
        var table = MlBulkTables.BuildWeeklyTable(rows);
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await DeleteInChunksAsync("SalarieSemaine", "SemaineDebut", bddSage, from, to, ct);
            await BulkInsertAsync(table, "SalarieSemaine", transaction, ct);
            await transaction.CommitAsync(ct);
        });
    }

    public Task<IReadOnlyList<MlSalarieJour>> GetDailyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default)
        => QueryDailyAsync(bddSage, from, to, ct);

    public Task<IReadOnlyList<MlSalarieSemaine>> GetWeeklyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default)
        => QueryWeeklyAsync(bddSage, from, to, ct);

    public async Task<IReadOnlyList<MlSalarieJour>> GetDailyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default)
    {
        // Pas de ORDER BY/Take côté SQL : un tri de plusieurs centaines de milliers de lignes
        // larges demande un grant mémoire que SQL Express ne peut pas toujours satisfaire
        // (attente RESOURCE_SEMAPHORE). Le plafond est appliqué en mémoire.
        var rows = await _db.SalarieJour
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage && x.Date >= from.Date && x.Date <= to.Date)
            .ToListAsync(ct);

        return rows.Count <= maxRows ? rows : rows.TakeLast(Math.Max(1, maxRows)).ToList();
    }

    public async Task<IReadOnlyList<MlSalarieSemaine>> GetWeeklyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default)
    {
        var rows = await _db.SalarieSemaine
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage && x.SemaineDebut >= from.Date && x.SemaineDebut <= to.Date)
            .ToListAsync(ct);

        return rows.Count <= maxRows ? rows : rows.TakeLast(Math.Max(1, maxRows)).ToList();
    }

    public async Task SaveModelAsync(MlModele model, CancellationToken ct = default)
    {
        _db.Modele.Add(model);
        await _db.SaveChangesAsync(ct);
    }

    public Task<MlModele?> GetActiveModelAsync(string bddSage, MlModelType type, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        return _db.Modele
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage && x.TypeModele == storageType && x.EstActif)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<MlModele>> ListModelsAsync(string bddSage, CancellationToken ct = default)
        => await _db.Modele
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage)
            .OrderByDescending(x => x.EntraineLe)
            .ToListAsync(ct);

    public async Task ReplacePredictionsAsync(
        string bddSage, MlModelType type, DateTime from, DateTime to,
        IReadOnlyCollection<MlPrediction> rows, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        await _db.Prediction
            .Where(x => x.BddSage == bddSage
                        && x.TypeModele == storageType
                        && x.Cible >= from.Date
                        && x.Cible <= to.Date)
            .ExecuteDeleteAsync(ct);

        await InsertInBatchesAsync(rows, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MlPrediction>> GetPredictionsAsync(
        string bddSage, MlModelType type, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        return await _db.Prediction
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage
                        && x.TypeModele == storageType
                        && x.Cible >= from.Date
                        && x.Cible <= to.Date)
            .OrderBy(x => x.Cible).ThenBy(x => x.Matricule)
            .ToListAsync(ct);
    }

    public Task ArchiveActiveModelsAsync(string bddSage, MlModelType type, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        return _db.Modele
            .Where(x => x.BddSage == bddSage && x.TypeModele == storageType && x.EstActif)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.EstActif, false)
                    .SetProperty(x => x.Statut, MlModelStatus.Archive),
                ct);
    }

    public async Task PurgeModelsAsync(string bddSage, MlModelType type, int keep, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        var toPurge = await _db.Modele
            .Where(x => x.BddSage == bddSage && x.TypeModele == storageType && !x.EstActif)
            .OrderByDescending(x => x.EntraineLe)
            .Skip(Math.Max(0, keep))
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (toPurge.Count == 0)
            return;

        // Les prévisions des semaines passées servent au suivi prévu / réel : un modèle encore
        // référencé garde sa ligne (sans le binaire, seule partie volumineuse) au lieu d'emporter
        // l'historique avec lui.
        var references = await _db.Prediction
            .Where(p => toPurge.Contains(p.ModeleId))
            .Select(p => p.ModeleId)
            .Distinct()
            .ToListAsync(ct);

        await _db.Modele
            .Where(m => references.Contains(m.Id) && m.ModeleBinaire != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ModeleBinaire, (byte[]?)null), ct);
        await _db.Modele
            .Where(m => toPurge.Contains(m.Id) && !references.Contains(m.Id))
            .ExecuteDeleteAsync(ct);
    }

    // ------------------------------------------------------------------ lecture

    private async Task<IReadOnlyList<MlSalarieJour>> QueryDailyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct)
        => await _db.SalarieJour
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage && x.Date >= from.Date && x.Date <= to.Date)
            .OrderBy(x => x.Matricule).ThenBy(x => x.Date)
            .ToListAsync(ct);

    private async Task<IReadOnlyList<MlSalarieSemaine>> QueryWeeklyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct)
        => await _db.SalarieSemaine
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage && x.SemaineDebut >= from.Date && x.SemaineDebut <= to.Date)
            .OrderBy(x => x.Matricule).ThenBy(x => x.SemaineDebut)
            .ToListAsync(ct);

    // ------------------------------------------------------------------ bulk copy

    /// <summary>
    /// Supprime la plage par petits lots. Un DELETE unique de plusieurs dizaines de milliers de
    /// lignes demande un grant mémoire important (mise à jour des index) et peut attendre
    /// indéfiniment sur RESOURCE_SEMAPHORE sur une instance SQL Express.
    /// </summary>
    private async Task DeleteInChunksAsync(
        string table, string dateColumn, string bddSage, DateTime from, DateTime to, CancellationToken ct)
    {
        // Les noms de table/colonne sont des constantes internes ; les valeurs sont paramétrées.
        var sql = $@"DELETE TOP (5000) FROM [ml].[{table}]
                     WHERE [BddSage] = {{0}}
                       AND [{dateColumn}] >= {{1}}
                       AND [{dateColumn}] <= {{2}}";

        int supprimes;
        do
        {
            supprimes = await _db.Database.ExecuteSqlRawAsync(
                sql, new object[] { bddSage, from.Date, to.Date }, ct);
        }
        while (supprimes > 0);
    }

    private async Task BulkInsertAsync(
        DataTable table, string tableName, IDbContextTransaction transaction, CancellationToken ct)
    {
        if (table.Rows.Count == 0)
            return;

        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var sqlTransaction = (SqlTransaction)transaction.GetDbTransaction();

        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, sqlTransaction)
        {
            DestinationTableName = $"[ml].[{tableName}]",
            BatchSize = 5000,
            BulkCopyTimeout = 300
        };

        foreach (DataColumn column in table.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

        await bulk.WriteToServerAsync(table, ct);
    }

    private async Task InsertInBatchesAsync<T>(IReadOnlyCollection<T> rows, CancellationToken ct) where T : class
    {
        foreach (var chunk in rows.Chunk(BatchSize))
        {
            await _db.Set<T>().AddRangeAsync(chunk, ct);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }
    }
}
