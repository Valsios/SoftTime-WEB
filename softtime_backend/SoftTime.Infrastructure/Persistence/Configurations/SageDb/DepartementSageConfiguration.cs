using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class DepartementSageConfiguration : IEntityTypeConfiguration<DepartementSage>
{
    public void Configure(EntityTypeBuilder<DepartementSage> builder)
    {
        builder.ToTable("T_DEPARTEMENT");
        builder.HasKey(x => x.Code);

        builder.Property(x => x.Code)
            .HasColumnName("Code")
            .IsRequired(true)
            .ValueGeneratedNever();

        builder.Property(x => x.Intitule)
            .HasColumnName("Intitule")
            .IsRequired(false);
    }
}
