using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_MAJORATIONConfiguration : IEntityTypeConfiguration<T_MAJORATION>
{
    public void Configure(EntityTypeBuilder<T_MAJORATION> builder)
    {
        builder.ToTable("T_MAJORATION");
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

        builder.Property(x => x.Cotation)
            .HasColumnName("Cotation")
            .HasPrecision(18, 2)
            .IsRequired(false);

        builder.Property(x => x.Mojoration)
            .HasColumnName("Mojoration")
            .IsRequired(false);
    }
}
