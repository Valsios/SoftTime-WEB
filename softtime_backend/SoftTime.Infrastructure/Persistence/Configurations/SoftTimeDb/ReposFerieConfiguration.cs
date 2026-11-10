using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class ReposFerieConfiguration : IEntityTypeConfiguration<ReposFerie>
{
    public void Configure(EntityTypeBuilder<ReposFerie> builder)
    {
        builder.ToTable("T_REST_FERIE");
        builder.HasKey(x => x.Id);
        ConfigureTextColumns(builder);
        ConfigureDateColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<ReposFerie> builder)
    {
        builder.Property(x => x.BddPointeuse).HasColumnName("BDD_POINTEUSE").IsRequired(false);
        builder.Property(x => x.BddSage).HasColumnName("BDD_SAGE").IsRequired(false);
        builder.Property(x => x.MatriculeSage).HasColumnName("MATRICULE_SAGE").IsRequired(false);
    }

    private static void ConfigureDateColumns(EntityTypeBuilder<ReposFerie> builder)
    {
        builder.Property(x => x.DateDebutSemaine).HasColumnName("DATE_DEBUT_SEMAINE").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<ReposFerie> builder)
    {
        builder.Property(x => x.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        builder.Property(x => x.Etat).HasColumnName("ETAT").IsRequired(false);
        builder.Property(x => x.FerieDebutSemaine).HasColumnName("FERIE_DEBUT_SEMAINE").IsRequired(false);
    }
}
