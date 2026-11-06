using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_impr_HSConfiguration : IEntityTypeConfiguration<T_impr_HS>
{
    public void Configure(EntityTypeBuilder<T_impr_HS> builder)
    {
        builder.ToTable("T_impr_HS");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.EXO130)
            .HasColumnName("EXO130")
            .IsRequired(false);

        builder.Property(x => x.EXO150)
            .HasColumnName("EXO150")
            .IsRequired(false);

        builder.Property(x => x.IMPO130)
            .HasColumnName("IMPO130")
            .IsRequired(false);

        builder.Property(x => x.IMPO150)
            .HasColumnName("IMPO150")
            .IsRequired(false);

        builder.Property(x => x.MATR)
            .HasColumnName("MATR")
            .IsRequired(false);

        builder.Property(x => x.MDIMANCHES)
            .HasColumnName("MDIMANCHES")
            .IsRequired(false);

        builder.Property(x => x.MFERIES)
            .HasColumnName("MFERIES")
            .IsRequired(false);

        builder.Property(x => x.MNUITS)
            .HasColumnName("MNUITS")
            .IsRequired(false);

        builder.Property(x => x.TOTALH)
            .HasColumnName("TOTALH")
            .IsRequired(false);
    }
}
