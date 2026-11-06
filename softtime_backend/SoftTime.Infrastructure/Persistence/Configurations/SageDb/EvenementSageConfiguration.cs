using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class EvenementSageConfiguration : IEntityTypeConfiguration<EvenementSage>
{
    public void Configure(EntityTypeBuilder<EvenementSage> builder)
    {
        builder.ToTable("T_GHR");
        builder.HasKey(x => x.CodeNE);

        builder.Property(x => x.CodeNE)
            .HasColumnName("CodeNE")
            .IsRequired(true)
            .ValueGeneratedNever();

        builder.Property(x => x.Intitule)
            .HasColumnName("Intitule")
            .IsRequired(false);
    }
}
