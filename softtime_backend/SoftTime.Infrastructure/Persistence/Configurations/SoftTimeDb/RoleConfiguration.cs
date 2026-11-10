using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("T_ROLE");
        builder.HasKey(x => x.IdRole);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<Role> builder)
    {
        builder.Property(x => x.NomRole).HasColumnName("NOMROLE").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<Role> builder)
    {
        builder.Property(x => x.IdRole).HasColumnName("IDROLE").ValueGeneratedOnAdd();
    }
}
