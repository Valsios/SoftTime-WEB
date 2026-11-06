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

        builder.Property(x => x.IdRole)
            .HasColumnName("IDROLE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.NomRole)
            .HasColumnName("NOMROLE")
            .IsRequired(false);
    }
}
