using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_HEUREDIMANCHEConfiguration : IEntityTypeConfiguration<T_HEUREDIMANCHE>
{
    public void Configure(EntityTypeBuilder<T_HEUREDIMANCHE> builder)
    {
        builder.ToTable("T_HEUREDIMANCHE");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.Date_Dim)
            .HasColumnName("Date_Dim")
            .IsRequired(false);

        builder.Property(x => x.HeureDebut)
            .HasColumnName("HeureDebut")
            .IsRequired(false);

        builder.Property(x => x.HeureFin)
            .HasColumnName("HeureFin")
            .IsRequired(false);

        builder.Property(x => x.MATRICULE_SAGE)
            .HasColumnName("MATRICULE_SAGE")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.Valeur)
            .HasColumnName("Valeur")
            .IsRequired(false);
    }
}
