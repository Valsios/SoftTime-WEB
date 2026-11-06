using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class SuiviConfigurationConfiguration : IEntityTypeConfiguration<SuiviConfiguration>
{
    public void Configure(EntityTypeBuilder<SuiviConfiguration> builder)
    {
        builder.ToTable("suivi_TBLCLN");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Bases)
            .HasColumnName("BASES")
            .IsRequired(false);

        builder.Property(x => x.Colonnes)
            .HasColumnName("COLONNES")
            .IsRequired(false);

        builder.Property(x => x.Etats)
            .HasColumnName("ETATS")
            .IsRequired(false);

        builder.Property(x => x.Tables)
            .HasColumnName("TABLES")
            .IsRequired(false);

        builder.Property(x => x.Types)
            .HasColumnName("TYPES")
            .IsRequired(false);
    }
}
