using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_DROITConfiguration : IEntityTypeConfiguration<T_DROIT>
{
    public void Configure(EntityTypeBuilder<T_DROIT> builder)
    {
        builder.ToTable("T_DROIT");
        builder.HasKey(x => x.IDDROIT);

        builder.Property(x => x.IDDROIT)
            .HasColumnName("IDDROIT")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.DROIT)
            .HasColumnName("DROIT")
            .IsRequired(true);
    }
}
