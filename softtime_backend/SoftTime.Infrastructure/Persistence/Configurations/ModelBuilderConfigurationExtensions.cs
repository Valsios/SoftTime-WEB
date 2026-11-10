using Microsoft.EntityFrameworkCore;

namespace SoftTime.Infrastructure.Persistence.Configurations;

/// <summary>
/// Applique les <see cref="IEntityTypeConfiguration{TEntity}"/> d'un seul contexte : chaque DbContext a son dossier
/// (SoftTimeDb, SageDb, PointeuseDb), car les trois bases partagent cet assembly.
/// </summary>
internal static class ModelBuilderConfigurationExtensions
{
    public static ModelBuilder ApplyConfigurationsOf<TMarker>(this ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TMarker).Assembly,
            type => type.Namespace == typeof(TMarker).Namespace);
}
