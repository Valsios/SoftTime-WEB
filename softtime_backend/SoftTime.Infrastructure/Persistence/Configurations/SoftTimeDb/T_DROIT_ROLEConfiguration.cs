using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_DROIT_ROLEConfiguration : IEntityTypeConfiguration<T_DROIT_ROLE>
{
    public void Configure(EntityTypeBuilder<T_DROIT_ROLE> builder)
    {
        builder.ToTable("T_DROIT_ROLE");
        builder.HasKey(x => x.IDDROITROLE);

        builder.Property(x => x.IDDROITROLE)
            .HasColumnName("IDDROITROLE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.IDDROIT)
            .HasColumnName("IDDROIT");

        builder.Property(x => x.IDROLE)
            .HasColumnName("IDROLE");
    }
}
