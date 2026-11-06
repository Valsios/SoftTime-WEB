using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("T_ROLE_PERMISSION");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.IdRole)
            .HasColumnName("IDROLE")
            .HasPrecision(18, 0);

        builder.Property(x => x.IsGranted)
            .HasColumnName("IS_GRANTED");

        builder.Property(x => x.PermissionCode)
            .HasColumnName("PERMISSION_CODE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.HasIndex(x => new { x.IdRole, x.PermissionCode })
            .HasDatabaseName("IX_T_ROLE_PERMISSION_IDROLE_PERMISSION_CODE")
            .IsUnique();
    }
}
