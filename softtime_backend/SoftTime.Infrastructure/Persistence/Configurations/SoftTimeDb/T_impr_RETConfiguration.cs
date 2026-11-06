using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_impr_RETConfiguration : IEntityTypeConfiguration<T_impr_RET>
{
    public void Configure(EntityTypeBuilder<T_impr_RET> builder)
    {
        builder.ToTable("T_impr_RET");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .IsRequired(false);

        builder.Property(x => x.DATE_POINTAGE)
            .HasColumnName("DATE_POINTAGE")
            .IsRequired(false);

        builder.Property(x => x.HEURE_ENTREE)
            .HasColumnName("HEURE_ENTREE")
            .IsRequired(false);

        builder.Property(x => x.MATRICULES)
            .HasColumnName("MATRICULES")
            .IsRequired(false);

        builder.Property(x => x.RETARDS)
            .HasColumnName("RETARDS")
            .IsRequired(false);
    }
}
