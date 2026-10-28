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

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CaptureChanges(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CaptureChanges(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        _capturedEntities.Clear();
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
        => _capturedEntities.Clear();

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
                journal.AddRange(CreateDetails(group.Key, entry, correlation));
                _capturedEntities.Add(entry.Entity);
            }
        }

        context.Set<suivi_JOURNAL>().AddRange(journal);
        return journal.Count;
    }

    private bool IsAuditable(EntityEntry entry)
        => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
           && !ExcludedTables.Contains(TableName(entry));

    private static string TableName(EntityEntry entry)
        => entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;

    private IEnumerable<suivi_JOURNAL> CreateDetails(string table, EntityEntry entry, Guid correlation)
    {
        var action = ActionFor(entry.State);
        foreach (var property in ChangedProperties(entry))
        {
            var masked = IsSensitive(property.Metadata.Name);
            yield return CreateEntry(
                table,
                KeyFor(entry),
                property.Metadata.GetColumnName() ?? property.Metadata.Name,
                action,
                masked ? "********" : OldValueFor(property, entry.State),
                masked ? "********" : NewValueFor(property, entry.State),
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

    private IEnumerable<PropertyEntry> ChangedProperties(EntityEntry entry)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey()?.Properties.ToHashSet() ?? [];
        return entry.Properties.Where(property =>
            !primaryKey.Contains(property.Metadata)
            && (entry.State != EntityState.Modified || (property.IsModified && ValuesDiffer(property))));
    }

    private static bool ValuesDiffer(PropertyEntry property)
    {
        var comparer = property.Metadata.GetValueComparer();
        return comparer is null
            ? !Equals(property.OriginalValue, property.CurrentValue)
            : !comparer.Equals(property.OriginalValue, property.CurrentValue);
    }

    private static string? KeyFor(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return null;
        return string.Join(";", key.Properties.Select(property =>
        {
            var value = entry.Property(property.Name);
            var source = entry.State == EntityState.Deleted ? value.OriginalValue : value.CurrentValue;
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
}
