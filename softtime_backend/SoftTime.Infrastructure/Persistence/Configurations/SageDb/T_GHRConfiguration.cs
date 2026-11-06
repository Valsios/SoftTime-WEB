using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class T_GHRConfiguration : IEntityTypeConfiguration<T_GHR>
{
    public void Configure(EntityTypeBuilder<T_GHR> builder)
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
