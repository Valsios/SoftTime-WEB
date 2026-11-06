using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_REFRESH_TOKENConfiguration : IEntityTypeConfiguration<T_REFRESH_TOKEN>
{
    public void Configure(EntityTypeBuilder<T_REFRESH_TOKEN> builder)
    {
        builder.ToTable("T_REFRESH_TOKEN");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.DATE_CREATION)
            .HasColumnName("DATE_CREATION");

        builder.Property(x => x.DATE_EXPIRATION)
            .HasColumnName("DATE_EXPIRATION");

        builder.Property(x => x.DATE_REVOCATION)
            .HasColumnName("DATE_REVOCATION")
            .IsRequired(false);

        builder.Property(x => x.FAMILLE)
            .HasColumnName("FAMILLE");

        builder.Property(x => x.IDRESPONSABLE)
            .HasColumnName("IDRESPONSABLE")
            .HasPrecision(18, 0);

        builder.Property(x => x.REMPLACE_PAR)
            .HasColumnName("REMPLACE_PAR")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired(false);

        builder.Property(x => x.TOKEN_HASH)
            .HasColumnName("TOKEN_HASH")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired(true);

        builder.HasIndex(x => x.TOKEN_HASH)
            .HasDatabaseName("IX_T_REFRESH_TOKEN_TOKEN_HASH")
            .IsUnique();
    }
}
