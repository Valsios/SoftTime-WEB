using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class suivi_TBLCLNConfiguration : IEntityTypeConfiguration<suivi_TBLCLN>
{
    public void Configure(EntityTypeBuilder<suivi_TBLCLN> builder)
    {
        builder.ToTable("suivi_TBLCLN");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BASES)
            .HasColumnName("BASES")
            .IsRequired(false);

        builder.Property(x => x.COLONNES)
            .HasColumnName("COLONNES")
            .IsRequired(false);

        builder.Property(x => x.ETATS)
            .HasColumnName("ETATS")
            .IsRequired(false);

        builder.Property(x => x.TABLES)
            .HasColumnName("TABLES")
            .IsRequired(false);

        builder.Property(x => x.TYPES)
            .HasColumnName("TYPES")
            .IsRequired(false);
    }
}
