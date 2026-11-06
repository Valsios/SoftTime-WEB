using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_BDD_POINTEUSEConfiguration : IEntityTypeConfiguration<T_BDD_POINTEUSE>
{
    public void Configure(EntityTypeBuilder<T_BDD_POINTEUSE> builder)
    {
        builder.ToTable("T_BDD_POINTEUSE");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ACTIVE)
            .HasColumnName("ACTIVE")
            .IsRequired(false);

        builder.Property(x => x.MAP_PUNCH_COL_DATETIME)
            .HasColumnName("MAP_PUNCH_COL_DATETIME")
            .IsRequired(false);

        builder.Property(x => x.MAP_PUNCH_COL_TYPE)
            .HasColumnName("MAP_PUNCH_COL_TYPE")
            .IsRequired(false);

        builder.Property(x => x.MAP_PUNCH_COL_USER_ID)
            .HasColumnName("MAP_PUNCH_COL_USER_ID")
            .IsRequired(false);

        builder.Property(x => x.MAP_PUNCH_TABLE)
            .HasColumnName("MAP_PUNCH_TABLE")
            .IsRequired(false);

        builder.Property(x => x.MAP_USER_COL_BADGE)
            .HasColumnName("MAP_USER_COL_BADGE")
            .IsRequired(false);

        builder.Property(x => x.MAP_USER_COL_ID)
            .HasColumnName("MAP_USER_COL_ID")
            .IsRequired(false);

        builder.Property(x => x.MAP_USER_COL_NOM)
            .HasColumnName("MAP_USER_COL_NOM")
            .IsRequired(false);

        builder.Property(x => x.MAP_USER_COL_SSN)
            .HasColumnName("MAP_USER_COL_SSN")
            .IsRequired(false);

        builder.Property(x => x.MAP_USER_TABLE)
            .HasColumnName("MAP_USER_TABLE")
            .IsRequired(false);

        builder.Property(x => x.NOM_BD)
            .HasColumnName("NOM_BD")
            .IsRequired(false);

        builder.Property(x => x.SERVEUR)
            .HasColumnName("SERVEUR")
            .IsRequired(false);

        builder.Property(x => x.TLOGIN)
            .HasColumnName("TLOGIN")
            .IsRequired(false);

        builder.Property(x => x.TMDP)
            .HasColumnName("TMDP")
            .IsRequired(false);

        builder.Property(x => x.TYPE_AUTH)
            .HasColumnName("TYPE_AUTH")
            .IsRequired(false);

        builder.Property(x => x.TYPE_BASE)
            .HasColumnName("TYPE_BASE")
            .IsRequired(true);

        builder.Property(x => x.TYPE_POINTAGE)
            .HasColumnName("TYPE_POINTAGE")
            .IsRequired(false);
    }
}
