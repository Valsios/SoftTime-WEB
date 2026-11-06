using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_ACTIVECORRESPConfiguration : IEntityTypeConfiguration<T_ACTIVECORRESP>
{
    public void Configure(EntityTypeBuilder<T_ACTIVECORRESP> builder)
    {
        builder.ToTable("T_ACTIVECORRESP");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Active)
            .HasColumnName("Active")
            .IsRequired(false);

        builder.Property(x => x.DATE_MODIF)
            .HasColumnName("DATE_MODIF")
            .IsRequired(false);

        builder.Property(x => x.DERNIERE_SYNC)
            .HasColumnName("DERNIERE_SYNC")
            .IsRequired(false);

        builder.Property(x => x.POINTEUSE_BDD)
            .HasColumnName("POINTEUSE_BDD")
            .IsRequired(false);

        builder.Property(x => x.POINTEUSE_SERVEUR)
            .HasColumnName("POINTEUSE_SERVEUR")
            .IsRequired(false);

        builder.Property(x => x.SAGE_BDD)
            .HasColumnName("SAGE_BDD")
            .IsRequired(false);

        builder.Property(x => x.SAGE_SERVEUR)
            .HasColumnName("SAGE_SERVEUR")
            .IsRequired(false);
    }
}
