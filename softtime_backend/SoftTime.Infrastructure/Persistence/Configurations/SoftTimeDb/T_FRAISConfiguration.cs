using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_FRAISConfiguration : IEntityTypeConfiguration<T_FRAIS>
{
    public void Configure(EntityTypeBuilder<T_FRAIS> builder)
    {
        builder.ToTable("T_FRAIS");
        builder.HasKey(x => x.IDPOINTAGE);

        builder.Property(x => x.IDPOINTAGE)
            .HasColumnName("IDPOINTAGE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.DATEDEB)
            .HasColumnName("DATEDEB")
            .IsRequired(false);

        builder.Property(x => x.DATEFIN)
            .HasColumnName("DATEFIN")
            .IsRequired(false);

        builder.Property(x => x.MATRICULE_SAGE)
            .HasColumnName("MATRICULE_SAGE")
            .HasMaxLength(64)
            .IsRequired(false);

        builder.Property(x => x.NOM_BDD_POINTEUSE)
            .HasColumnName("NOM_BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.NOM_BDD_SAGE)
            .HasColumnName("NOM_BDD_SAGE")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(x => x.TOTAL_POINT)
            .HasColumnName("TOTAL_POINT")
            .IsRequired(false);
    }
}
