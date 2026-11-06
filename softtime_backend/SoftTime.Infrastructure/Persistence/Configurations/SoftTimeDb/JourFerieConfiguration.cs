using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class JourFerieConfiguration : IEntityTypeConfiguration<JourFerie>
{
    public void Configure(EntityTypeBuilder<JourFerie> builder)
    {
        builder.ToTable("T_FERIE");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.BddSage)
            .HasColumnName("BDD_SAGE")
            .IsRequired(false);

        builder.Property(x => x.Date)
            .HasColumnName("DATE")
            .IsRequired(false);

        builder.Property(x => x.Intitule)
            .HasColumnName("INTITULE")
            .IsRequired(false);
    }
}
