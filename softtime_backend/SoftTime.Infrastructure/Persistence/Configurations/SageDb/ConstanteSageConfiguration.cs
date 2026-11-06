using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class ConstanteSageConfiguration : IEntityTypeConfiguration<ConstanteSage>
{
    public void Configure(EntityTypeBuilder<ConstanteSage> builder)
    {
        builder.ToTable("T_CST");
        builder.HasKey(x => new { x.CodeConstante, x.NoOrdre });

        builder.Property(x => x.CodeConstante)
            .HasColumnName("CodeConstante")
            .IsRequired(true)
            .ValueGeneratedNever();

        builder.Property(x => x.NoOrdre)
            .HasColumnName("NoOrdre")
            .ValueGeneratedNever();

        builder.Property(x => x.CodeOperande1)
            .HasColumnName("CodeOperande1")
            .IsRequired(false);

        builder.Property(x => x.Intitule)
            .HasColumnName("Intitule")
            .IsRequired(false);
    }
}
