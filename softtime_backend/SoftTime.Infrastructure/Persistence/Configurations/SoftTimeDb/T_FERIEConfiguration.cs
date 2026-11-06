using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_FERIEConfiguration : IEntityTypeConfiguration<T_FERIE>
{
    public void Configure(EntityTypeBuilder<T_FERIE> builder)
    {
        builder.ToTable("T_FERIE");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BDD_SAGE)
            .HasColumnName("BDD_SAGE")
            .IsRequired(false);

        builder.Property(x => x.DATE)
            .HasColumnName("DATE")
            .IsRequired(false);

        builder.Property(x => x.INTITULE)
            .HasColumnName("INTITULE")
            .IsRequired(false);
    }
}
