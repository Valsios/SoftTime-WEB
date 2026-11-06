using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class CategorieSalarieConfiguration : IEntityTypeConfiguration<CategorieSalarie>
{
    public void Configure(EntityTypeBuilder<CategorieSalarie> builder)
    {
        builder.ToTable("T_CAT_SAL");
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

        builder.Property(x => x.IdCateg)
            .HasColumnName("ID_CATEG");

        builder.Property(x => x.MatriculeSage)
            .HasColumnName("MATRICULE_SAGE");
    }
}
