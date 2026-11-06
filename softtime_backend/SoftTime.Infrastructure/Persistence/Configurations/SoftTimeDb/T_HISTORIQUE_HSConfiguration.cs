using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_HISTORIQUE_HSConfiguration : IEntityTypeConfiguration<T_HISTORIQUE_HS>
{
    public void Configure(EntityTypeBuilder<T_HISTORIQUE_HS> builder)
    {
        builder.ToTable("T_HISTORIQUE_HS");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ABSENCE)
            .HasColumnName("ABSENCE")
            .IsRequired(false);

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.DATE_SUPPRESSION)
            .HasColumnName("DATE_SUPPRESSION");

        builder.Property(x => x.DIM)
            .HasColumnName("DIM")
            .IsRequired(false);

        builder.Property(x => x.EXO130)
            .HasColumnName("EXO130")
            .IsRequired(false);

        builder.Property(x => x.EXO150)
            .HasColumnName("EXO150")
            .IsRequired(false);

        builder.Property(x => x.FERIE)
            .HasColumnName("FERIE")
            .IsRequired(false);

        builder.Property(x => x.HS_ID)
            .HasColumnName("HS_ID")
            .IsRequired(false);

        builder.Property(x => x.I130)
            .HasColumnName("I130")
            .IsRequired(false);

        builder.Property(x => x.I150)
            .HasColumnName("I150")
            .IsRequired(false);

        builder.Property(x => x.MATRICULE)
            .HasColumnName("MATRICULE")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.NUIT)
            .HasColumnName("NUIT")
            .IsRequired(false);

        builder.Property(x => x.NUM_SAL)
            .HasColumnName("NUM_SAL")
            .IsRequired(false);

        builder.Property(x => x.PERIODE_DEBUT)
            .HasColumnName("PERIODE_DEBUT")
            .IsRequired(false);

        builder.Property(x => x.PERIODE_FIN)
            .HasColumnName("PERIODE_FIN")
            .IsRequired(false);

        builder.Property(x => x.RETARD)
            .HasColumnName("RETARD")
            .IsRequired(false);
    }
}
