using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class RefreshTokenRecordConfiguration : IEntityTypeConfiguration<RefreshTokenRecord>
{
    public void Configure(EntityTypeBuilder<RefreshTokenRecord> builder)
    {
        builder.ToTable("T_REFRESH_TOKEN");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.DateCreation)
            .HasColumnName("DATE_CREATION");

        builder.Property(x => x.DateExpiration)
            .HasColumnName("DATE_EXPIRATION");

        builder.Property(x => x.DateRevocation)
            .HasColumnName("DATE_REVOCATION")
            .IsRequired(false);

        builder.Property(x => x.Famille)
            .HasColumnName("FAMILLE");

        builder.Property(x => x.IdResponsable)
            .HasColumnName("IDRESPONSABLE")
            .HasPrecision(18, 0);

        builder.Property(x => x.RemplacePar)
            .HasColumnName("REMPLACE_PAR")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired(false);

        builder.Property(x => x.TokenHash)
            .HasColumnName("TOKEN_HASH")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired(true);

        builder.HasIndex(x => x.TokenHash)
            .HasDatabaseName("IX_T_REFRESH_TOKEN_TOKEN_HASH")
            .IsUnique();
    }
}
