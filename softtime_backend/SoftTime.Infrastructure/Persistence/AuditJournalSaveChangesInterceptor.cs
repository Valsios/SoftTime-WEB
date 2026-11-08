using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

using SoftTime.Application.Abstractions;
using SoftTime.Application.Security;
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
    private readonly ICurrentUser _user;
    private readonly ICompanyContext _company;
    private readonly AuditJournalOptions _options;
    private readonly HashSet<object> _capturedEntities = new(ReferenceEqualityComparer.Instance);

    public AuditJournalSaveChangesInterceptor(
        ICurrentUser user,
        ICompanyContext company,
        IOptions<AuditJournalOptions> options)
    {
        _user = user;
        _company = company;
        _options = options.Value;
    }

    /// <summary>
    /// Tables jamais journalisées : journaux eux-mêmes, historiques, <c>T_AUDIT_DROIT</c> qui est déjà le journal
    /// spécialisé des changements de droits, et <c>T_REFRESH_TOKEN</c> (une ligne par connexion et par rafraîchissement).
    /// </summary>
    private static readonly HashSet<string> ExcludedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "suivi_SUIVI", "suivi_T", "suivi_TBLCLN", "suivi_JOURNAL",
        "T_HISTORIQUE_POINTAGE", "T_HISTORIQUE_ANOMALIE", "T_HISTORIQUE_HS",
        "T_AUDIT_DROIT", "T_REFRESH_TOKEN"
    };

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
        var journal = new List<JournalAudit>();
        foreach (var group in entries.GroupBy(AuditValueFormat.TableName, StringComparer.OrdinalIgnoreCase))
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

        context.Set<JournalAudit>().AddRange(journal);
        return journal.Count;
    }

    private AuditBatch PrepareBatch(SoftTimeDbContext context)
    {
        var entries = context.ChangeTracker.Entries().Where(IsAuditable).ToList();
        if (entries.Count == 0)
            return AuditBatch.Empty;

        var correlation = Guid.NewGuid();
        var beforeSave = new List<JournalAudit>();
        var deferredAdditions = new List<DeferredAddition>();
        foreach (var group in entries.GroupBy(AuditValueFormat.TableName, StringComparer.OrdinalIgnoreCase))
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
        context.Set<JournalAudit>().AddRange(batch.BeforeSave);
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
        context.Set<JournalAudit>().AddRange(batch.BeforeSave);
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
        context.Set<JournalAudit>().AddRange(journal);
    }

    private bool IsAuditable(EntityEntry entry)
        => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
           && !ExcludedTables.Contains(AuditValueFormat.TableName(entry));

    private IEnumerable<JournalAudit> CreateDetails(
        string table,
        EntityEntry entry,
        Guid correlation,
        EntityState state)
    {
        var action = AuditValueFormat.ActionFor(state);
        foreach (var property in ChangedProperties(entry, state))
        {
            var column = property.Metadata.GetColumnName() ?? property.Metadata.Name;
            var masked = AuditMaskingPolicy.IsSensitive(table, column);
            yield return Stamp(new JournalAudit
            {
                Tables = table,
                Cles = AuditMaskingPolicy.MaskKey(table, AuditValueFormat.KeyFor(entry, state)),
                Colonnes = column,
                Actions = action,
                Anciens = masked ? AuditMaskingPolicy.Mask : AuditValueFormat.OldValueFor(property, state),
                Nouveaux = masked ? AuditMaskingPolicy.Mask : AuditValueFormat.NewValueFor(property, state)
            }, correlation);
        }
    }

    private JournalAudit CreateSummary(string table, IEnumerable<EntityEntry> entries, Guid correlation)
    {
        var list = entries.ToList();
        var inserted = list.Count(e => e.State == EntityState.Added);
        var modified = list.Count(e => e.State == EntityState.Modified);
        var deleted = list.Count(e => e.State == EntityState.Deleted);
        return Stamp(new JournalAudit
        {
            Tables = table,
            Actions = "Modification",
            Nouveaux = $"Synthèse volume : Insertion={inserted}; Modification={modified}; Suppression={deleted}"
        }, correlation);
    }

    private IEnumerable<PropertyEntry> ChangedProperties(EntityEntry entry, EntityState state)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey()?.Properties.ToHashSet() ?? [];
        return entry.Properties.Where(property =>
            !primaryKey.Contains(property.Metadata)
            && (state != EntityState.Modified || (property.IsModified && AuditValueFormat.ValuesDiffer(property))));
    }

    /// <summary>Complète une ligne du journal : horodatage, auteur, bases du tenant et corrélation de la sauvegarde.</summary>
    private JournalAudit Stamp(JournalAudit entry, Guid correlation)
    {
        entry.DateUtc = DateTime.UtcNow;
        entry.Users = string.IsNullOrWhiteSpace(_user.Login) ? "SYSTEME:SoftTimeDbContext" : _user.Login;
        entry.BaseSage = _company.SageDatabase;
        entry.BasePointeuse = _company.PointeuseDatabase;
        entry.Correlations = correlation;
        entry.Source = "EF:SoftTimeDbContext";
        return entry;
    }

    private sealed record DeferredAddition(string Table, EntityEntry Entry, Guid Correlation);

    private sealed record AuditBatch(
        List<JournalAudit> BeforeSave,
        List<DeferredAddition> DeferredAdditions)
    {
        public static AuditBatch Empty { get; } = new([], []);
        public bool IsEmpty => BeforeSave.Count == 0 && DeferredAdditions.Count == 0;
    }
}
