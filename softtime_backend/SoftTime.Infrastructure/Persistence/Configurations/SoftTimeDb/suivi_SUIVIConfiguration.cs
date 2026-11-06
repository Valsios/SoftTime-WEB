using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class suivi_SUIVIConfiguration : IEntityTypeConfiguration<suivi_SUIVI>
{
    public void Configure(EntityTypeBuilder<suivi_SUIVI> builder)
    {
        builder.ToTable("suivi_SUIVI");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x._ACTIONS)
            .HasColumnName("_ACTIONS")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x._ANCIENS)
            .HasColumnName("_ANCIENS")
            .IsRequired(false);

        builder.Property(x => x._BASES)
            .HasColumnName("_BASES")
            .IsRequired(false);

        builder.Property(x => x._COLONNES)
            .HasColumnName("_COLONNES")
            .IsRequired(false);

        builder.Property(x => x._DATETIMES)
            .HasColumnName("_DATETIMES")
            .IsRequired(false);

        builder.Property(x => x._NOUVEAUX)
            .HasColumnName("_NOUVEAUX")
            .IsRequired(false);

        builder.Property(x => x._TABLES)
            .HasColumnName("_TABLES")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x._USERS)
            .HasColumnName("_USERS")
            .IsRequired(false);
    }
}
