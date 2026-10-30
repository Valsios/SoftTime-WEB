using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SoftTime.Application.Abstractions;
using SoftTime.Domain.Entities.Ml;
using SoftTime.Domain.Models.Ml;

namespace SoftTime.Infrastructure.Persistence;

/// <summary>
/// Implémentation EF Core de la base analytique. Les remplacements de jour/semaine sont
/// idempotents (suppression de la plage puis réinsertion) et utilisent <c>SqlBulkCopy</c> :
/// l'ETL d'un backfill manipule des centaines de milliers de lignes, pour lesquelles une
/// insertion ENTITY par ENTITY serait trop lente.
/// </summary>
public sealed class EfMlStore : IMlStore
{
    private const int BatchSize = 2000;
    private readonly MlDbContext _db;

    public EfMlStore(MlDbContext db) => _db = db;

    public Task EnsureSchemaAsync(CancellationToken ct = default) => _db.Database.MigrateAsync(ct);

    public async Task ReplaceDailyAsync(
        string bddSage, DateTime from, DateTime to,
        IReadOnlyCollection<T_ML_SALARIE_JOUR> rows, CancellationToken ct = default)
    {
        var table = BuildDailyTable(rows);
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
        IReadOnlyCollection<T_ML_SALARIE_SEMAINE> rows, CancellationToken ct = default)
    {
        var table = BuildWeeklyTable(rows);
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await DeleteInChunksAsync("SalarieSemaine", "SemaineDebut", bddSage, from, to, ct);
            await BulkInsertAsync(table, "SalarieSemaine", transaction, ct);
            await transaction.CommitAsync(ct);
        });
    }

    public Task<IReadOnlyList<T_ML_SALARIE_JOUR>> GetDailyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default)
        => QueryDailyAsync(bddSage, from, to, ct);

    public Task<IReadOnlyList<T_ML_SALARIE_SEMAINE>> GetWeeklyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct = default)
        => QueryWeeklyAsync(bddSage, from, to, ct);

    public async Task<IReadOnlyList<T_ML_SALARIE_JOUR>> GetDailyForTrainingAsync(
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

    public async Task<IReadOnlyList<T_ML_SALARIE_SEMAINE>> GetWeeklyForTrainingAsync(
        string bddSage, DateTime from, DateTime to, int maxRows, CancellationToken ct = default)
    {
        var rows = await _db.SalarieSemaine
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage && x.SemaineDebut >= from.Date && x.SemaineDebut <= to.Date)
            .ToListAsync(ct);

        return rows.Count <= maxRows ? rows : rows.TakeLast(Math.Max(1, maxRows)).ToList();
    }

    public async Task SaveModelAsync(T_ML_MODELE model, CancellationToken ct = default)
    {
        _db.Modele.Add(model);
        await _db.SaveChangesAsync(ct);
    }

    public Task<T_ML_MODELE?> GetActiveModelAsync(string bddSage, MlModelType type, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        return _db.Modele
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage && x.TypeModele == storageType && x.EstActif)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<T_ML_MODELE>> ListModelsAsync(string bddSage, CancellationToken ct = default)
        => await _db.Modele
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage)
            .OrderByDescending(x => x.EntraineLe)
            .ToListAsync(ct);

    public async Task ReplacePredictionsAsync(
        string bddSage, MlModelType type, DateTime from, DateTime to, int modelId,
        IReadOnlyCollection<T_ML_PREDICTION> rows, CancellationToken ct = default)
    {
        var storageType = MlModelTypes.ToStorage(type);
        await _db.Prediction
            .Where(x => x.BddSage == bddSage
                        && x.TypeModele == storageType
                        && x.ModeleId == modelId
                        && x.Cible >= from.Date
                        && x.Cible <= to.Date)
            .ExecuteDeleteAsync(ct);

        await InsertInBatchesAsync(rows, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<T_ML_PREDICTION>> GetPredictionsAsync(
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

        await _db.Prediction.Where(p => toPurge.Contains(p.ModeleId)).ExecuteDeleteAsync(ct);
        await _db.Modele.Where(m => toPurge.Contains(m.Id)).ExecuteDeleteAsync(ct);
    }

    // ------------------------------------------------------------------ lecture

    private async Task<IReadOnlyList<T_ML_SALARIE_JOUR>> QueryDailyAsync(
        string bddSage, DateTime from, DateTime to, CancellationToken ct)
        => await _db.SalarieJour
            .AsNoTracking()
            .Where(x => x.BddSage == bddSage && x.Date >= from.Date && x.Date <= to.Date)
            .OrderBy(x => x.Matricule).ThenBy(x => x.Date)
            .ToListAsync(ct);

    private async Task<IReadOnlyList<T_ML_SALARIE_SEMAINE>> QueryWeeklyAsync(
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

    private static DataTable BuildDailyTable(IReadOnlyCollection<T_ML_SALARIE_JOUR> rows)
    {
        var table = new DataTable();
        table.Columns.Add("BddSage", typeof(string));
        table.Columns.Add("Matricule", typeof(string));
        table.Columns.Add("Date", typeof(DateTime));
        table.Columns.Add("EstJourPlanifie", typeof(bool));
        table.Columns.Add("EstOff", typeof(bool));
        table.Columns.Add("EstFerie", typeof(bool));
        table.Columns.Add("EstWeekend", typeof(bool));
        table.Columns.Add("EstConge", typeof(bool));
        table.Columns.Add("PlanningConnu", typeof(bool));
        table.Columns.Add("HeuresPlanifiees", typeof(decimal));
        table.Columns.Add("HeuresTravaillees", typeof(decimal));
        table.Columns.Add("RetardMinutes", typeof(int));
        table.Columns.Add("AbsenceNonJustifiee", typeof(bool));
        table.Columns.Add("AbsenceDeclaree", typeof(bool));
        table.Columns.Add("AbsenceHeures", typeof(decimal));
        table.Columns.Add("JourSemaine", typeof(byte));
        table.Columns.Add("SemaineIso", typeof(byte));
        table.Columns.Add("AnneeIso", typeof(short));
        table.Columns.Add("Mois", typeof(byte));
        table.Columns.Add("JourDuMois", typeof(byte));
        table.Columns.Add("VeilleFerie", typeof(bool));
        table.Columns.Add("LendemainFerie", typeof(bool));
        table.Columns.Add("DebutMois", typeof(bool));
        table.Columns.Add("FinMois", typeof(bool));
        table.Columns.Add("CategorieId", typeof(int));
        table.Columns.Add("Departement", typeof(string));
        table.Columns.Add("HeuresSemaineContrat", typeof(decimal));
        table.Columns.Add("MultiPointage", typeof(bool));
        table.Columns.Add("AncienneteJours", typeof(int));
        table.Columns.Add("Absences7j", typeof(int));
        table.Columns.Add("Absences30j", typeof(int));
        table.Columns.Add("Absences90j", typeof(int));
        table.Columns.Add("TauxAbsence30j", typeof(double));
        table.Columns.Add("Retards30j", typeof(int));
        table.Columns.Add("JoursTravailles7j", typeof(int));
        table.Columns.Add("JoursDepuisDerniereAbsence", typeof(int));
        table.Columns.Add("HeuresSupp4Semaines", typeof(decimal));
        table.Columns.Add("TauxAbsenceEquipe30j", typeof(double));
        table.Columns.Add("EffectifPrevuJour", typeof(int));
        table.Columns.Add("CongesPrevusJour", typeof(int));
        table.Columns.Add("SourceGenereeLe", typeof(DateTime));
        table.Columns.Add("VersionSchema", typeof(string));

        foreach (var entity in rows)
        {
            var row = table.NewRow();
            row["BddSage"] = entity.BddSage;
            row["Matricule"] = entity.Matricule;
            row["Date"] = entity.Date;
            row["EstJourPlanifie"] = entity.EstJourPlanifie;
            row["EstOff"] = entity.EstOff;
            row["EstFerie"] = entity.EstFerie;
            row["EstWeekend"] = entity.EstWeekend;
            row["EstConge"] = entity.EstConge;
            row["PlanningConnu"] = entity.PlanningConnu;
            row["HeuresPlanifiees"] = Value(entity.HeuresPlanifiees);
            row["HeuresTravaillees"] = Value(entity.HeuresTravaillees);
            row["RetardMinutes"] = Value(entity.RetardMinutes);
            row["AbsenceNonJustifiee"] = entity.AbsenceNonJustifiee;
            row["AbsenceDeclaree"] = entity.AbsenceDeclaree;
            row["AbsenceHeures"] = Value(entity.AbsenceHeures);
            row["JourSemaine"] = entity.JourSemaine;
            row["SemaineIso"] = entity.SemaineIso;
            row["AnneeIso"] = entity.AnneeIso;
            row["Mois"] = entity.Mois;
            row["JourDuMois"] = entity.JourDuMois;
            row["VeilleFerie"] = entity.VeilleFerie;
            row["LendemainFerie"] = entity.LendemainFerie;
            row["DebutMois"] = entity.DebutMois;
            row["FinMois"] = entity.FinMois;
            row["CategorieId"] = Value(entity.CategorieId);
            row["Departement"] = Value(entity.Departement);
            row["HeuresSemaineContrat"] = Value(entity.HeuresSemaineContrat);
            row["MultiPointage"] = entity.MultiPointage;
            row["AncienneteJours"] = Value(entity.AncienneteJours);
            row["Absences7j"] = entity.Absences7j;
            row["Absences30j"] = entity.Absences30j;
            row["Absences90j"] = entity.Absences90j;
            row["TauxAbsence30j"] = entity.TauxAbsence30j;
            row["Retards30j"] = entity.Retards30j;
            row["JoursTravailles7j"] = entity.JoursTravailles7j;
            row["JoursDepuisDerniereAbsence"] = Value(entity.JoursDepuisDerniereAbsence);
            row["HeuresSupp4Semaines"] = entity.HeuresSupp4Semaines;
            row["TauxAbsenceEquipe30j"] = entity.TauxAbsenceEquipe30j;
            row["EffectifPrevuJour"] = entity.EffectifPrevuJour;
            row["CongesPrevusJour"] = entity.CongesPrevusJour;
            row["SourceGenereeLe"] = entity.SourceGenereeLe;
            row["VersionSchema"] = entity.VersionSchema;
            table.Rows.Add(row);
        }

        return table;
    }

    private static DataTable BuildWeeklyTable(IReadOnlyCollection<T_ML_SALARIE_SEMAINE> rows)
    {
        var table = new DataTable();
        table.Columns.Add("BddSage", typeof(string));
        table.Columns.Add("Matricule", typeof(string));
        table.Columns.Add("SemaineDebut", typeof(DateTime));
        table.Columns.Add("SemaineIso", typeof(byte));
        table.Columns.Add("AnneeIso", typeof(short));
        table.Columns.Add("HeuresPlanifiees", typeof(decimal));
        table.Columns.Add("HeuresTravaillees", typeof(decimal));
        table.Columns.Add("NbJoursPlanifies", typeof(int));
        table.Columns.Add("NbJoursTravailles", typeof(int));
        table.Columns.Add("CongeJours", typeof(decimal));
        table.Columns.Add("HS", typeof(decimal));
        table.Columns.Add("HSValidee", typeof(bool));
        table.Columns.Add("Nuit", typeof(decimal));
        table.Columns.Add("Dimanche", typeof(decimal));
        table.Columns.Add("Ferie", typeof(decimal));
        table.Columns.Add("RetardMinutes", typeof(int));
        table.Columns.Add("AbsenceHeures", typeof(decimal));
        table.Columns.Add("HsMoyenne4Semaines", typeof(decimal));
        table.Columns.Add("HsMoyenne8Semaines", typeof(decimal));
        table.Columns.Add("Absences4Semaines", typeof(int));
        table.Columns.Add("Retards4Semaines", typeof(int));
        table.Columns.Add("HeuresTravaillees4Semaines", typeof(decimal));
        table.Columns.Add("CategorieId", typeof(int));
        table.Columns.Add("Departement", typeof(string));
        table.Columns.Add("HeuresSemaineContrat", typeof(decimal));
        table.Columns.Add("MultiPointage", typeof(bool));
        table.Columns.Add("SourceGenereeLe", typeof(DateTime));
        table.Columns.Add("VersionSchema", typeof(string));

        foreach (var entity in rows)
        {
            var row = table.NewRow();
            row["BddSage"] = entity.BddSage;
            row["Matricule"] = entity.Matricule;
            row["SemaineDebut"] = entity.SemaineDebut;
            row["SemaineIso"] = entity.SemaineIso;
            row["AnneeIso"] = entity.AnneeIso;
            row["HeuresPlanifiees"] = entity.HeuresPlanifiees;
            row["HeuresTravaillees"] = entity.HeuresTravaillees;
            row["NbJoursPlanifies"] = entity.NbJoursPlanifies;
            row["NbJoursTravailles"] = entity.NbJoursTravailles;
            row["CongeJours"] = entity.CongeJours;
            row["HS"] = entity.HS;
            row["HSValidee"] = entity.HSValidee;
            row["Nuit"] = entity.Nuit;
            row["Dimanche"] = entity.Dimanche;
            row["Ferie"] = entity.Ferie;
            row["RetardMinutes"] = entity.RetardMinutes;
            row["AbsenceHeures"] = entity.AbsenceHeures;
            row["HsMoyenne4Semaines"] = entity.HsMoyenne4Semaines;
            row["HsMoyenne8Semaines"] = entity.HsMoyenne8Semaines;
            row["Absences4Semaines"] = entity.Absences4Semaines;
            row["Retards4Semaines"] = entity.Retards4Semaines;
            row["HeuresTravaillees4Semaines"] = entity.HeuresTravaillees4Semaines;
            row["CategorieId"] = Value(entity.CategorieId);
            row["Departement"] = Value(entity.Departement);
            row["HeuresSemaineContrat"] = Value(entity.HeuresSemaineContrat);
            row["MultiPointage"] = entity.MultiPointage;
            row["SourceGenereeLe"] = entity.SourceGenereeLe;
            row["VersionSchema"] = entity.VersionSchema;
            table.Rows.Add(row);
        }

        return table;
    }

    private static object Value(object? value) => value ?? DBNull.Value;

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
