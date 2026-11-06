using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class JournalAuditConfiguration : IEntityTypeConfiguration<JournalAudit>
{
    public void Configure(EntityTypeBuilder<JournalAudit> builder)
    {
        builder.ToTable("suivi_JOURNAL");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("Id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Actions)
            .HasColumnName("ACTIONS")
            .HasMaxLength(32)
            .IsRequired(false);

        builder.Property(x => x.Anciens)
            .HasColumnName("ANCIENS")
            .IsRequired(false);

        builder.Property(x => x.BasePointeuse)
            .HasColumnName("BASE_POINTEUSE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.BaseSage)
            .HasColumnName("BASE_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.Cles)
            .HasColumnName("CLES")
            .HasMaxLength(450)
            .IsRequired(false);

        builder.Property(x => x.Colonnes)
            .HasColumnName("COLONNES")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.Correlations)
            .HasColumnName("CORRELATIONS");

        builder.Property(x => x.DateUtc)
            .HasColumnName("DATE_UTC")
            .HasColumnType("datetime2");

        builder.Property(x => x.Nouveaux)
            .HasColumnName("NOUVEAUX")
            .IsRequired(false);

        builder.Property(x => x.Source)
            .HasColumnName("SOURCE")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.Tables)
            .HasColumnName("TABLES")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.Users)
            .HasColumnName("USERS")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.HasIndex(x => new { x.Correlations, x.Id })
            .HasDatabaseName("IX_suivi_JOURNAL_Correlation");

        builder.HasIndex(x => new { x.DateUtc, x.Id })
            .HasDatabaseName("IX_suivi_JOURNAL_Date")
            .IsDescending();

        builder.HasIndex(x => new { x.BaseSage, x.DateUtc, x.Id })
            .HasDatabaseName("IX_suivi_JOURNAL_Base_Date")
            .IsDescending(false, true, true);

        builder.HasIndex(x => new { x.Tables, x.Actions, x.DateUtc, x.Id })
            .HasDatabaseName("IX_suivi_JOURNAL_Table_Action_Date")
            .IsDescending(false, false, true, true);
    }
}
