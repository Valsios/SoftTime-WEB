using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class T_GHRCAL_SOCIETEConfiguration : IEntityTypeConfiguration<T_GHRCAL_SOCIETE>
{
    public void Configure(EntityTypeBuilder<T_GHRCAL_SOCIETE> builder)
    {
        builder.ToTable("T_GHRCAL_SOCIETE");
        builder.HasKey(x => x.IdGHRCal);

        builder.Property(x => x.IdGHRCal)
            .HasColumnName("IdGHRCal")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ApresMidi)
            .HasColumnName("ApresMidi")
            .IsRequired(false);

        builder.Property(x => x.EtatJour)
            .HasColumnName("EtatJour")
            .IsRequired(false);

        builder.Property(x => x.Matin)
            .HasColumnName("Matin")
            .IsRequired(false);

        builder.Property(x => x.PeriodeDebut)
            .HasColumnName("PeriodeDebut")
            .IsRequired(false);

        builder.Property(x => x.PeriodeFin)
            .HasColumnName("PeriodeFin")
            .IsRequired(false);
    }
}
