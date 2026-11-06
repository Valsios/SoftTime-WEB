using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_impr_ABSConfiguration : IEntityTypeConfiguration<T_impr_ABS>
{
    public void Configure(EntityTypeBuilder<T_impr_ABS> builder)
    {
        builder.ToTable("T_impr_ABS");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ABSENCE_AM)
            .HasColumnName("ABSENCE_AM")
            .IsRequired(false);

        builder.Property(x => x.ABSENCE_PM)
            .HasColumnName("ABSENCE_PM")
            .IsRequired(false);

        builder.Property(x => x.BDD_POINTEUSE)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .IsRequired(false);

        builder.Property(x => x.DATE_POINTAGE)
            .HasColumnName("DATE_POINTAGE")
            .IsRequired(false);

        builder.Property(x => x.INTITULE_ABS)
            .HasColumnName("INTITULE_ABS")
            .IsRequired(false);

        builder.Property(x => x.MATRICULES)
            .HasColumnName("MATRICULES")
            .IsRequired(false);
    }
}
