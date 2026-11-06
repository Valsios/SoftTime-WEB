using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_USER_PERMISSIONConfiguration : IEntityTypeConfiguration<T_USER_PERMISSION>
{
    public void Configure(EntityTypeBuilder<T_USER_PERMISSION> builder)
    {
        builder.ToTable("T_USER_PERMISSION");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.IDRESPONSABLE)
            .HasColumnName("IDRESPONSABLE")
            .HasPrecision(18, 0);

        builder.Property(x => x.IS_GRANTED)
            .HasColumnName("IS_GRANTED");

        builder.Property(x => x.PERMISSION_CODE)
            .HasColumnName("PERMISSION_CODE")
            .HasMaxLength(128)
            .IsRequired(true);

        builder.HasIndex(x => new { x.IDRESPONSABLE, x.PERMISSION_CODE })
            .HasDatabaseName("IX_T_USER_PERMISSION_IDRESPONSABLE_PERMISSION_CODE")
            .IsUnique();
    }
}
