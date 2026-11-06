using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_CANTINEConfiguration : IEntityTypeConfiguration<T_CANTINE>
{
    public void Configure(EntityTypeBuilder<T_CANTINE> builder)
    {
        builder.ToTable("T_CANTINE");
        builder.HasKey(x => x.IDCANTINE);

        builder.Property(x => x.IDCANTINE)
            .HasColumnName("IDCANTINE")
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

        builder.Property(x => x.TOTAL_CANTINE)
            .HasColumnName("TOTAL_CANTINE")
            .IsRequired(false);
    }
}
