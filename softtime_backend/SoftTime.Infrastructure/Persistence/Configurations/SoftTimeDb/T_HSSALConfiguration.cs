using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_HSSALConfiguration : IEntityTypeConfiguration<T_HSSAL>
{
    public void Configure(EntityTypeBuilder<T_HSSAL> builder)
    {
        builder.ToTable("T_HSSAL");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Date)
            .HasColumnName("Date")
            .IsRequired(false);

        builder.Property(x => x.HT)
            .HasColumnName("HT")
            .IsRequired(false);

        builder.Property(x => x.IDSAL)
            .HasColumnName("IDSAL")
            .IsRequired(false);
    }
}
