using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_RESPONSABLEConfiguration : IEntityTypeConfiguration<T_RESPONSABLE>
{
    public void Configure(EntityTypeBuilder<T_RESPONSABLE> builder)
    {
        builder.ToTable("T_RESPONSABLE");
        builder.HasKey(x => x.IDRESPONSABLE);

        builder.Property(x => x.IDRESPONSABLE)
            .HasColumnName("IDRESPONSABLE")
            .HasPrecision(18, 0)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.EMAIL)
            .HasColumnName("EMAIL")
            .IsRequired(false);

        builder.Property(x => x.IDROLE)
            .HasColumnName("IDROLE")
            .HasPrecision(18, 0);

        builder.Property(x => x.LOGIN)
            .HasColumnName("LOGIN")
            .IsRequired(false);

        builder.Property(x => x.MATRICULE)
            .HasColumnName("MATRICULE")
            .IsRequired(false);

        builder.Property(x => x.NOMRESPONSABLE)
            .HasColumnName("NOMRESPONSABLE")
            .IsRequired(false);

        builder.Property(x => x.PASSWORD)
            .HasColumnName("PASSWORD")
            .IsRequired(false);

        builder.Property(x => x.TENTATIVES_ECHOUEES)
            .HasColumnName("TENTATIVES_ECHOUEES");

        builder.Property(x => x.VERROUILLE_JUSQUA)
            .HasColumnName("VERROUILLE_JUSQUA")
            .IsRequired(false);
    }
}
