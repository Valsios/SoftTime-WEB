using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class T_GHRSALConfiguration : IEntityTypeConfiguration<T_GHRSAL>
{
    public void Configure(EntityTypeBuilder<T_GHRSAL> builder)
    {
        builder.ToTable("T_GHRSAL");
        builder.HasKey(x => x.IdGhrSal);

        builder.Property(x => x.IdGhrSal)
            .HasColumnName("IdGhrSal")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ApresMidi)
            .HasColumnName("ApresMidi")
            .IsRequired(false);

        builder.Property(x => x.CodeNE)
            .HasColumnName("CodeNE")
            .IsRequired(true);

        builder.Property(x => x.Matin)
            .HasColumnName("Matin")
            .IsRequired(false);

        builder.Property(x => x.NombreParJour)
            .HasColumnName("NombreParJour")
            .IsRequired(false);

        builder.Property(x => x.NumSalarie)
            .HasColumnName("NumSalarie");

        builder.Property(x => x.PeriodeDebut)
            .HasColumnName("PeriodeDebut")
            .IsRequired(false);

        builder.Property(x => x.PeriodeFin)
            .HasColumnName("PeriodeFin")
            .IsRequired(false);

        builder.Property(x => x.Valeur)
            .HasColumnName("Valeur")
            .IsRequired(false);
    }
}
