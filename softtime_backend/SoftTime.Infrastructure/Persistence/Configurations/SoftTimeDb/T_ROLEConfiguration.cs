using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_ROLEConfiguration : IEntityTypeConfiguration<T_ROLE>
{
    public void Configure(EntityTypeBuilder<T_ROLE> builder)
    {
        builder.ToTable("T_ROLE");
        builder.HasKey(x => x.IDROLE);

        builder.Property(x => x.IDROLE)
            .HasColumnName("IDROLE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.NOMROLE)
            .HasColumnName("NOMROLE")
            .IsRequired(true);
    }
}
