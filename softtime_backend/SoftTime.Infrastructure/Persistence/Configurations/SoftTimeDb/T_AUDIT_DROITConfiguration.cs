using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_AUDIT_DROITConfiguration : IEntityTypeConfiguration<T_AUDIT_DROIT>
{
    public void Configure(EntityTypeBuilder<T_AUDIT_DROIT> builder)
    {
        builder.ToTable("T_AUDIT_DROIT");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ACTION)
            .HasColumnName("ACTION")
            .HasMaxLength(50)
            .IsRequired(true);

        builder.Property(x => x.DATE_ACTION)
            .HasColumnName("DATE_ACTION");

        builder.Property(x => x.ID_CIBLE)
            .HasColumnName("ID_CIBLE")
            .HasPrecision(18, 0);

        builder.Property(x => x.ID_OPERATEUR)
            .HasColumnName("ID_OPERATEUR")
            .HasPrecision(18, 0);

        builder.Property(x => x.LOGIN_OPERATEUR)
            .HasColumnName("LOGIN_OPERATEUR")
            .HasMaxLength(100)
            .IsRequired(true);

        builder.Property(x => x.NOM_CIBLE)
            .HasColumnName("NOM_CIBLE")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(x => x.PERMS_APRES)
            .HasColumnName("PERMS_APRES")
            .IsRequired(false);

        builder.Property(x => x.PERMS_AVANT)
            .HasColumnName("PERMS_AVANT")
            .IsRequired(false);

        builder.Property(x => x.TYPE_CIBLE)
            .HasColumnName("TYPE_CIBLE")
            .HasMaxLength(20)
            .IsRequired(true);
    }
}
