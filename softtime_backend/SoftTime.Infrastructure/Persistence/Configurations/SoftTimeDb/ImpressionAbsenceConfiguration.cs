using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class ImpressionAbsenceConfiguration : IEntityTypeConfiguration<ImpressionAbsence>
{
    public void Configure(EntityTypeBuilder<ImpressionAbsence> builder)
    {
        builder.ToTable("T_impr_ABS");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.AbsenceAm)
            .HasColumnName("ABSENCE_AM")
            .IsRequired(false);

        builder.Property(x => x.AbsencePm)
            .HasColumnName("ABSENCE_PM")
            .IsRequired(false);

        builder.Property(x => x.BddPointeuse)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BddSage)
            .HasColumnName("BDD_SAGE")
            .IsRequired(false);

        builder.Property(x => x.DatePointage)
            .HasColumnName("DATE_POINTAGE")
            .IsRequired(false);

        builder.Property(x => x.IntituleAbs)
            .HasColumnName("INTITULE_ABS")
            .IsRequired(false);

        builder.Property(x => x.Matricules)
            .HasColumnName("MATRICULES")
            .IsRequired(false);
    }
}
