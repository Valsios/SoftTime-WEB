using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class ResponsableConfiguration : IEntityTypeConfiguration<Responsable>
{
    public void Configure(EntityTypeBuilder<Responsable> builder)
    {
        builder.ToTable("T_RESPONSABLE");
        builder.HasKey(x => x.IdResponsable);

        builder.Property(x => x.IdResponsable)
            .HasColumnName("IDRESPONSABLE")
            .HasPrecision(18, 0)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Email)
            .HasColumnName("EMAIL")
            .IsRequired(false);

        builder.Property(x => x.IdRole)
            .HasColumnName("IDROLE")
            .HasPrecision(18, 0);

        builder.Property(x => x.Login)
            .HasColumnName("LOGIN")
            .IsRequired(false);

        builder.Property(x => x.Matricule)
            .HasColumnName("MATRICULE")
            .IsRequired(false);

        builder.Property(x => x.NomResponsable)
            .HasColumnName("NOMRESPONSABLE")
            .IsRequired(false);

        builder.Property(x => x.Password)
            .HasColumnName("PASSWORD")
            .IsRequired(false);

        builder.Property(x => x.TentativesEchouees)
            .HasColumnName("TENTATIVES_ECHOUEES");

        builder.Property(x => x.VerrouilleJusqua)
            .HasColumnName("VERROUILLE_JUSQUA")
            .IsRequired(false);
    }
}
