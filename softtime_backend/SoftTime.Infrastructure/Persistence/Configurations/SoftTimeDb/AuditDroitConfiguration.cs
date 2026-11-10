using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class AuditDroitConfiguration : IEntityTypeConfiguration<AuditDroit>
{
    public void Configure(EntityTypeBuilder<AuditDroit> builder)
    {
        builder.ToTable("T_AUDIT_DROIT");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<AuditDroit> builder)
    {
        builder.Property(x => x.Action).HasColumnName("ACTION").HasMaxLength(50).IsRequired(false);
        builder.Property(x => x.LoginOperateur).HasColumnName("LOGIN_OPERATEUR").HasMaxLength(100).IsRequired(false);
        builder.Property(x => x.NomCible).HasColumnName("NOM_CIBLE").HasMaxLength(100).IsRequired(false);
        builder.Property(x => x.PermsApres).HasColumnName("PERMS_APRES").IsRequired(false);
        builder.Property(x => x.PermsAvant).HasColumnName("PERMS_AVANT").IsRequired(false);
        builder.Property(x => x.TypeCible).HasColumnName("TYPE_CIBLE").HasMaxLength(20).IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<AuditDroit> builder)
    {
        builder.Property(x => x.DateAction).HasColumnName("DATE_ACTION");
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<AuditDroit> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.IdCible).HasColumnName("ID_CIBLE").HasPrecision(18, 0);
        builder.Property(x => x.IdOperateur).HasColumnName("ID_OPERATEUR").HasPrecision(18, 0);
    }
}
