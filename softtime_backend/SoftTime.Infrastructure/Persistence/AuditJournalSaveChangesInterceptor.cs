using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SoftTime.Application.Abstractions;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence;

/// <summary>
/// Ajoute le journal applicatif à la même sauvegarde EF que les données métier.
/// Une erreur de persistance annule donc l'ensemble de l'opération, sans journal
/// orphelin ni donnée métier partiellement validée.
/// </summary>
public sealed class AuditJournalSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> ExcludedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "suivi_SUIVI", "suivi_T", "suivi_TBLCLN", "suivi_JOURNAL",
        "T_HISTORIQUE_POINTAGE", "T_HISTORIQUE_ANOMALIE", "T_HISTORIQUE_HS"
    };

    private static readonly string[] SensitiveFragments =
        ["PASS", "MDP", "PWD", "HASH", "SALT", "TOKEN", "SECRET", "CONN", "KEY", "JETON"];

    private readonly ICurrentUser _user;
    private readonly ICompanyContext _company;
    private readonly AuditJournalOptions _options;
    private readonly HashSet<object> _capturedEntities = new(ReferenceEqualityComparer.Instance);

    public AuditJournalSaveChangesInterceptor(ICurrentUser user, ICompanyContext company, AuditJournalOptions options)
    {
        _user = user;
        _company = company;
        _options = options;
    }

    /// <summary>
    /// Sauvegarde métier et audit dans une transaction commune. Les insertions sont différées
    /// jusqu'à ce que SQL Server ait matérialisé les clés d'identité.
    /// </summary>
    public int SaveChanges(SoftTimeDbContext context, bool acceptAllChangesOnSuccess)
    {
        if (!_options.Enabled)
            return context.SaveChangesWithoutAudit(acceptAllChangesOnSuccess);

        var batch = PrepareBatch(context);
        if (batch.IsEmpty)
            return context.SaveChangesWithoutAudit(acceptAllChangesOnSuccess);

        if (context.Database.CurrentTransaction is not null)
            return PersistWithinTransaction(context, batch, acceptAllChangesOnSuccess);

        return context.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var transaction = context.Database.BeginTransaction();
            try
            {
                var result = PersistWithinTransaction(context, batch, acceptAllChangesOnSuccess);
                transaction.Commit();
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        });
    }

    public async Task<int> SaveChangesAsync(
        SoftTimeDbContext context,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return await context.SaveChangesWithoutAuditAsync(acceptAllChangesOnSuccess, cancellationToken);

        var batch = PrepareBatch(context);
        if (batch.IsEmpty)
            return await context.SaveChangesWithoutAuditAsync(acceptAllChangesOnSuccess, cancellationToken);

        if (context.Database.CurrentTransaction is not null)
            return await PersistWithinTransactionAsync(context, batch, acceptAllChangesOnSuccess, cancellationToken);

        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await PersistWithinTransactionAsync(context, batch, acceptAllChangesOnSuccess, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    /// <summary>Construit les entrées de journal sans exécuter de sauvegarde distincte.</summary>
    public int CaptureChanges(DbContext? context)
    {
        if (context is not SoftTimeDbContext)
            return 0;

        var entries = context.ChangeTracker.Entries()
            .Where(entry => IsAuditable(entry) && !_capturedEntities.Contains(entry.Entity))
            .ToList();
        if (entries.Count == 0)
            return 0;

        var correlation = Guid.NewGuid();
        var journal = new List<suivi_JOURNAL>();
        foreach (var group in entries.GroupBy(TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > _options.DetailThreshold)
            {
                journal.Add(CreateSummary(group.Key, group, correlation));
                foreach (var entry in group)
                    _capturedEntities.Add(entry.Entity);
                continue;
            }

            foreach (var entry in group)
            {
                journal.AddRange(CreateDetails(group.Key, entry, correlation, entry.State));
                _capturedEntities.Add(entry.Entity);
            }
        }

        context.Set<suivi_JOURNAL>().AddRange(journal);
        return journal.Count;
    }

    private AuditBatch PrepareBatch(SoftTimeDbContext context)
    {
        var entries = context.ChangeTracker.Entries().Where(IsAuditable).ToList();
        if (entries.Count == 0)
            return AuditBatch.Empty;

        var correlation = Guid.NewGuid();
        var beforeSave = new List<suivi_JOURNAL>();
        var deferredAdditions = new List<DeferredAddition>();
        foreach (var group in entries.GroupBy(TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > _options.DetailThreshold)
            {
                beforeSave.Add(CreateSummary(group.Key, group, correlation));
                continue;
            }

            foreach (var entry in group)
            {
                if (entry.State == EntityState.Added)
                    deferredAdditions.Add(new DeferredAddition(group.Key, entry, correlation));
                else
                    beforeSave.AddRange(CreateDetails(group.Key, entry, correlation, entry.State));
            }
        }

        return new AuditBatch(beforeSave, deferredAdditions);
    }

    private int PersistWithinTransaction(SoftTimeDbContext context, AuditBatch batch, bool acceptAllChangesOnSuccess)
    {
        context.Set<suivi_JOURNAL>().AddRange(batch.BeforeSave);
        var result = context.SaveChangesWithoutAudit(acceptAllChangesOnSuccess);
        WriteDeferredAdditions(context, batch);
        if (batch.DeferredAdditions.Count > 0)
            context.SaveChangesWithoutAudit(acceptAllChangesOnSuccess);
        return result;
    }

    private async Task<int> PersistWithinTransactionAsync(
        SoftTimeDbContext context,
        AuditBatch batch,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        context.Set<suivi_JOURNAL>().AddRange(batch.BeforeSave);
        var result = await context.SaveChangesWithoutAuditAsync(acceptAllChangesOnSuccess, cancellationToken);
        WriteDeferredAdditions(context, batch);
        if (batch.DeferredAdditions.Count > 0)
            await context.SaveChangesWithoutAuditAsync(acceptAllChangesOnSuccess, cancellationToken);
        return result;
    }

    private void WriteDeferredAdditions(SoftTimeDbContext context, AuditBatch batch)
    {
        if (batch.DeferredAdditions.Count == 0)
            return;

        var journal = batch.DeferredAdditions.SelectMany(addition =>
            CreateDetails(addition.Table, addition.Entry, addition.Correlation, EntityState.Added));
        context.Set<suivi_JOURNAL>().AddRange(journal);
    }

    private bool IsAuditable(EntityEntry entry)
        => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
           && !ExcludedTables.Contains(TableName(entry));

    private static string TableName(EntityEntry entry)
        => entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;

    private IEnumerable<suivi_JOURNAL> CreateDetails(
        string table,
        EntityEntry entry,
        Guid correlation,
        EntityState state)
    {
        var action = ActionFor(state);
        foreach (var property in ChangedProperties(entry, state))
        {
            var masked = IsSensitive(property.Metadata.Name);
            yield return CreateEntry(
                table,
                KeyFor(entry, state),
                property.Metadata.GetColumnName() ?? property.Metadata.Name,
                action,
                masked ? "********" : OldValueFor(property, state),
                masked ? "********" : NewValueFor(property, state),
                correlation);
        }
    }

    private suivi_JOURNAL CreateSummary(string table, IEnumerable<EntityEntry> entries, Guid correlation)
    {
        var list = entries.ToList();
        var inserted = list.Count(e => e.State == EntityState.Added);
        var modified = list.Count(e => e.State == EntityState.Modified);
        var deleted = list.Count(e => e.State == EntityState.Deleted);
        return CreateEntry(
            table,
            null,
            null,
            "Modification",
            null,
            $"Synthèse volume : Insertion={inserted}; Modification={modified}; Suppression={deleted}",
            correlation);
    }

    private IEnumerable<PropertyEntry> ChangedProperties(EntityEntry entry, EntityState state)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey()?.Properties.ToHashSet() ?? [];
        return entry.Properties.Where(property =>
            !primaryKey.Contains(property.Metadata)
            && (state != EntityState.Modified || (property.IsModified && ValuesDiffer(property))));
    }

    private static bool ValuesDiffer(PropertyEntry property)
    {
        var comparer = property.Metadata.GetValueComparer();
        return comparer is null
            ? !Equals(property.OriginalValue, property.CurrentValue)
            : !comparer.Equals(property.OriginalValue, property.CurrentValue);
    }

    private static string? KeyFor(EntityEntry entry, EntityState state)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return null;
        return string.Join(";", key.Properties.Select(property =>
        {
            var value = entry.Property(property.Name);
            var source = state == EntityState.Deleted ? value.OriginalValue : value.CurrentValue;
            return $"{property.GetColumnName() ?? property.Name}={Format(source)}";
        }));
    }

    private static string? ValueFor(PropertyEntry property, bool original)
        => Format(original ? property.OriginalValue : property.CurrentValue);

    private static string? OldValueFor(PropertyEntry property, EntityState state)
        => state == EntityState.Added ? null : ValueFor(property, true);

    private static string? NewValueFor(PropertyEntry property, EntityState state)
        => state == EntityState.Deleted ? null : ValueFor(property, false);

    private static string? Format(object? value)
        => value switch
        {
            null => null,
            DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static bool IsSensitive(string name)
        => SensitiveFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string ActionFor(EntityState state) => state switch
    {
        EntityState.Added => "Insertion",
        EntityState.Deleted => "Suppression",
        _ => "Modification"
    };

    private suivi_JOURNAL CreateEntry(
        string table,
        string? key,
        string? column,
        string action,
        string? oldValue,
        string? newValue,
        Guid correlation)
        => new()
        {
            DATE_UTC = DateTime.UtcNow,
            TABLES = table,
            CLES = key,
            COLONNES = column,
            ACTIONS = action,
            ANCIENS = oldValue,
            NOUVEAUX = newValue,
            USERS = string.IsNullOrWhiteSpace(_user.Login) ? "SYSTEME:SoftTimeDbContext" : _user.Login,
            BASE_SAGE = _company.SageDatabase,
            BASE_POINTEUSE = _company.PointeuseDatabase,
            CORRELATIONS = correlation,
            SOURCE = "EF:SoftTimeDbContext"
        };

    private sealed record DeferredAddition(string Table, EntityEntry Entry, Guid Correlation);

    private sealed record AuditBatch(
        List<suivi_JOURNAL> BeforeSave,
        List<DeferredAddition> DeferredAdditions)
    {
        public static AuditBatch Empty { get; } = new([], []);
        public bool IsEmpty => BeforeSave.Count == 0 && DeferredAdditions.Count == 0;
    }
}
