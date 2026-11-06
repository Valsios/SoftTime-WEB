using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.Sage;

namespace SoftTime.Infrastructure.Persistence.Configurations.SageDb;

public sealed class T_DEPARTEMENTConfiguration : IEntityTypeConfiguration<T_DEPARTEMENT>
{
    public void Configure(EntityTypeBuilder<T_DEPARTEMENT> builder)
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
