using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("T_USER_PERMISSION");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
        ConfigureIndexes(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<UserPermission> builder)
    {
        builder.Property(x => x.PermissionCode).HasColumnName("PERMISSION_CODE").HasMaxLength(128).IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<UserPermission> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.IdResponsable).HasColumnName("IDRESPONSABLE").HasPrecision(18, 0);
        builder.Property(x => x.IsGranted).HasColumnName("IS_GRANTED");
        builder.Property(x => x.IsDeviation).HasColumnName("IS_DEVIATION");
    }

    private static void ConfigureIndexes(EntityTypeBuilder<UserPermission> builder)
    {
        builder.HasIndex(x => new { x.IdResponsable, x.PermissionCode })
            .HasDatabaseName("IX_T_USER_PERMISSION_IDRESPONSABLE_PERMISSION_CODE")
            .IsUnique()
            .HasFilter("[PERMISSION_CODE] IS NOT NULL");
    }
}
