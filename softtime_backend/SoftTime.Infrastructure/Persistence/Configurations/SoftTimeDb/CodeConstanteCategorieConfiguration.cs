using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class CodeConstanteCategorieConfiguration : IEntityTypeConfiguration<CodeConstanteCategorie>
{
    public void Configure(EntityTypeBuilder<CodeConstanteCategorie> builder)
    {
        builder.ToTable("T_CODE_CONSTANTE");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Categorie)
            .HasColumnName("CATEGORIE")
            .IsRequired(false);

        builder.Property(x => x.CodeConstante)
            .HasColumnName("CODE_CONSTANTE")
            .IsRequired(false);

        builder.Property(x => x.Intitule)
            .HasColumnName("INTITULE")
            .IsRequired(false);
    }
}
