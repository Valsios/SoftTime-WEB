using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class CorrespondanceActiveConfiguration : IEntityTypeConfiguration<CorrespondanceActive>
{
    public void Configure(EntityTypeBuilder<CorrespondanceActive> builder)
    {
        builder.ToTable("T_ACTIVECORRESP");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Active)
            .HasColumnName("Active")
            .IsRequired(false);

        builder.Property(x => x.DateModif)
            .HasColumnName("DATE_MODIF")
            .IsRequired(false);

        builder.Property(x => x.DerniereSync)
            .HasColumnName("DERNIERE_SYNC")
            .IsRequired(false);

        builder.Property(x => x.PointeuseBdd)
            .HasColumnName("POINTEUSE_BDD")
            .IsRequired(false);

        builder.Property(x => x.PointeuseServeur)
            .HasColumnName("POINTEUSE_SERVEUR")
            .IsRequired(false);

        builder.Property(x => x.SageBdd)
            .HasColumnName("SAGE_BDD")
            .IsRequired(false);

        builder.Property(x => x.SageServeur)
            .HasColumnName("SAGE_SERVEUR")
            .IsRequired(false);
    }
}
