using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class DroitRoleConfiguration : IEntityTypeConfiguration<DroitRole>
{
    public void Configure(EntityTypeBuilder<DroitRole> builder)
    {
        builder.ToTable("T_DROIT_ROLE");
        builder.HasKey(x => x.IdDroitRole);

        builder.Property(x => x.IdDroitRole)
            .HasColumnName("IDDROITROLE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.IdDroit)
            .HasColumnName("IDDROIT");

        builder.Property(x => x.IdRole)
            .HasColumnName("IDROLE");
    }
}
