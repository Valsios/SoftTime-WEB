using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class SuiviModificationConfiguration : IEntityTypeConfiguration<SuiviModification>
{
    public void Configure(EntityTypeBuilder<SuiviModification> builder)
    {
        builder.ToTable("suivi_SUIVI");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Actions)
            .HasColumnName("_ACTIONS")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.Anciens)
            .HasColumnName("_ANCIENS")
            .IsRequired(false);

        builder.Property(x => x.Bases)
            .HasColumnName("_BASES")
            .IsRequired(false);

        builder.Property(x => x.Colonnes)
            .HasColumnName("_COLONNES")
            .IsRequired(false);

        builder.Property(x => x.Datetimes)
            .HasColumnName("_DATETIMES")
            .IsRequired(false);

        builder.Property(x => x.Nouveaux)
            .HasColumnName("_NOUVEAUX")
            .IsRequired(false);

        builder.Property(x => x.Tables)
            .HasColumnName("_TABLES")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.Users)
            .HasColumnName("_USERS")
            .IsRequired(false);
    }
}
