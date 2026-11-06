using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_BDD_AUTORISEConfiguration : IEntityTypeConfiguration<T_BDD_AUTORISE>
{
    public void Configure(EntityTypeBuilder<T_BDD_AUTORISE> builder)
    {
        builder.ToTable("T_BDD_AUTORISE");
        builder.HasKey(x => x.IDBDDAUTORISE);

        builder.Property(x => x.IDBDDAUTORISE)
            .HasColumnName("IDBDDAUTORISE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.IDBDD)
            .HasColumnName("IDBDD");

        builder.Property(x => x.IDRESPONSABLE)
            .HasColumnName("IDRESPONSABLE");
    }
}
