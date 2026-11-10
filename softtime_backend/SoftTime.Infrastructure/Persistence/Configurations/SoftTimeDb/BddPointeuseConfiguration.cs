using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class BddPointeuseConfiguration : IEntityTypeConfiguration<BddPointeuse>
{
    public void Configure(EntityTypeBuilder<BddPointeuse> builder)
    {
        builder.ToTable("T_BDD_POINTEUSE");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<BddPointeuse> builder)
    {
        builder.Property(x => x.MapPunchColDatetime).HasColumnName("MAP_PUNCH_COL_DATETIME").IsRequired(false);
        builder.Property(x => x.MapPunchColType).HasColumnName("MAP_PUNCH_COL_TYPE").IsRequired(false);
        builder.Property(x => x.MapPunchColUserId).HasColumnName("MAP_PUNCH_COL_USER_ID").IsRequired(false);
        builder.Property(x => x.MapPunchTable).HasColumnName("MAP_PUNCH_TABLE").IsRequired(false);
        builder.Property(x => x.MapUserColBadge).HasColumnName("MAP_USER_COL_BADGE").IsRequired(false);
        builder.Property(x => x.MapUserColId).HasColumnName("MAP_USER_COL_ID").IsRequired(false);
        builder.Property(x => x.MapUserColNom).HasColumnName("MAP_USER_COL_NOM").IsRequired(false);
        builder.Property(x => x.MapUserColSsn).HasColumnName("MAP_USER_COL_SSN").IsRequired(false);
        builder.Property(x => x.MapUserTable).HasColumnName("MAP_USER_TABLE").IsRequired(false);
        builder.Property(x => x.NomBd).HasColumnName("NOM_BD").IsRequired(false);
        builder.Property(x => x.Serveur).HasColumnName("SERVEUR").IsRequired(false);
        builder.Property(x => x.TLogin).HasColumnName("TLOGIN").IsRequired(false);
        builder.Property(x => x.TMdp).HasColumnName("TMDP").IsRequired(false);
        builder.Property(x => x.TypeBase).HasColumnName("TYPE_BASE").IsRequired(false);
        builder.Property(x => x.TypePointage).HasColumnName("TYPE_POINTAGE").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<BddPointeuse> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.Active).HasColumnName("ACTIVE").IsRequired(false);
        builder.Property(x => x.TypeAuth).HasColumnName("TYPE_AUTH").IsRequired(false);
    }
}
