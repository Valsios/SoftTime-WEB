using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_CAT_SALConfiguration : IEntityTypeConfiguration<T_CAT_SAL>
{
    public void Configure(EntityTypeBuilder<T_CAT_SAL> builder)
    {
        builder.ToTable("T_CAT_SAL");
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

        builder.Property(x => x.ID_CATEG)
            .HasColumnName("ID_CATEG");

        builder.Property(x => x.MATRICULE_SAGE)
            .HasColumnName("MATRICULE_SAGE");
    }
}
