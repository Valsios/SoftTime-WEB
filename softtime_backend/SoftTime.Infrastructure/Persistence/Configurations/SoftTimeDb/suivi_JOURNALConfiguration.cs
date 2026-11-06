using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class suivi_JOURNALConfiguration : IEntityTypeConfiguration<suivi_JOURNAL>
{
    public void Configure(EntityTypeBuilder<suivi_JOURNAL> builder)
    {
        builder.ToTable("suivi_JOURNAL");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ACTIONS)
            .HasColumnName("ACTIONS")
            .HasMaxLength(32)
            .IsRequired(true);

        builder.Property(x => x.ANCIENS)
            .HasColumnName("ANCIENS")
            .IsRequired(false);

        builder.Property(x => x.BASE_POINTEUSE)
            .HasColumnName("BASE_POINTEUSE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.BASE_SAGE)
            .HasColumnName("BASE_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.CLES)
            .HasColumnName("CLES")
            .HasMaxLength(450)
            .IsRequired(false);

        builder.Property(x => x.COLONNES)
            .HasColumnName("COLONNES")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.CORRELATIONS)
            .HasColumnName("CORRELATIONS");

        builder.Property(x => x.DATE_UTC)
            .HasColumnName("DATE_UTC")
            .HasColumnType("datetime2");

        builder.Property(x => x.NOUVEAUX)
            .HasColumnName("NOUVEAUX")
            .IsRequired(false);

        builder.Property(x => x.SOURCE)
            .HasColumnName("SOURCE")
            .HasMaxLength(64)
            .IsRequired(true);

        builder.Property(x => x.TABLES)
            .HasColumnName("TABLES")
            .HasMaxLength(128)
            .IsRequired(true);

        builder.Property(x => x.USERS)
            .HasColumnName("USERS")
            .HasMaxLength(256)
            .IsRequired(true);

        builder.HasIndex(x => new { x.CORRELATIONS, x.Id })
            .HasDatabaseName("IX_suivi_JOURNAL_Correlation");

        builder.HasIndex(x => new { x.DATE_UTC, x.Id })
            .HasDatabaseName("IX_suivi_JOURNAL_Date")
            .IsDescending();

        builder.HasIndex(x => new { x.BASE_SAGE, x.DATE_UTC, x.Id })
            .HasDatabaseName("IX_suivi_JOURNAL_Base_Date")
            .IsDescending(false, true, true);

        builder.HasIndex(x => new { x.TABLES, x.ACTIONS, x.DATE_UTC, x.Id })
            .HasDatabaseName("IX_suivi_JOURNAL_Table_Action_Date")
            .IsDescending(false, false, true, true);
    }
}
