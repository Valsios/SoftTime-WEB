using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class HsSalarieConfiguration : IEntityTypeConfiguration<HsSalarie>
{
    public void Configure(EntityTypeBuilder<HsSalarie> builder)
    {
        builder.ToTable("T_HSSAL");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Date)
            .HasColumnName("Date")
            .IsRequired(false);

        builder.Property(x => x.Ht)
            .HasColumnName("HT")
            .IsRequired(false);

        builder.Property(x => x.IdSal)
            .HasColumnName("IDSAL")
            .IsRequired(false);
    }
}
