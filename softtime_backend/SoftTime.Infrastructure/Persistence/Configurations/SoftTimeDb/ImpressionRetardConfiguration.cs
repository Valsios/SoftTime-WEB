using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class ImpressionRetardConfiguration : IEntityTypeConfiguration<ImpressionRetard>
{
    public void Configure(EntityTypeBuilder<ImpressionRetard> builder)
    {
        builder.ToTable("T_impr_RET");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BddPointeuse)
            .HasColumnName("BDD_POINTEUSE")
            .IsRequired(false);

        builder.Property(x => x.BddSage)
            .HasColumnName("BDD_SAGE")
            .IsRequired(false);

        builder.Property(x => x.DatePointage)
            .HasColumnName("DATE_POINTAGE")
            .IsRequired(false);

        builder.Property(x => x.HeureEntree)
            .HasColumnName("HEURE_ENTREE")
            .IsRequired(false);

        builder.Property(x => x.Matricules)
            .HasColumnName("MATRICULES")
            .IsRequired(false);

        builder.Property(x => x.Retards)
            .HasColumnName("RETARDS")
            .IsRequired(false);
    }
}
