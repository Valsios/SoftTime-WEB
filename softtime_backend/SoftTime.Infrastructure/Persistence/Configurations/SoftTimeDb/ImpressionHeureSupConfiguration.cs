using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class ImpressionHeureSupConfiguration : IEntityTypeConfiguration<ImpressionHeureSup>
{
    public void Configure(EntityTypeBuilder<ImpressionHeureSup> builder)
    {
        builder.ToTable("T_impr_HS");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Exo130)
            .HasColumnName("EXO130")
            .IsRequired(false);

        builder.Property(x => x.Exo150)
            .HasColumnName("EXO150")
            .IsRequired(false);

        builder.Property(x => x.Impo130)
            .HasColumnName("IMPO130")
            .IsRequired(false);

        builder.Property(x => x.Impo150)
            .HasColumnName("IMPO150")
            .IsRequired(false);

        builder.Property(x => x.Matr)
            .HasColumnName("MATR")
            .IsRequired(false);

        builder.Property(x => x.MDimanches)
            .HasColumnName("MDIMANCHES")
            .IsRequired(false);

        builder.Property(x => x.MFeries)
            .HasColumnName("MFERIES")
            .IsRequired(false);

        builder.Property(x => x.MNuits)
            .HasColumnName("MNUITS")
            .IsRequired(false);

        builder.Property(x => x.TotalH)
            .HasColumnName("TOTALH")
            .IsRequired(false);
    }
}
