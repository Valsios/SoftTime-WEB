using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_CODE_CONSTANTEConfiguration : IEntityTypeConfiguration<T_CODE_CONSTANTE>
{
    public void Configure(EntityTypeBuilder<T_CODE_CONSTANTE> builder)
    {
        builder.ToTable("T_CODE_CONSTANTE");
        builder.HasKey(x => x.ID);

        builder.Property(x => x.ID)
            .HasColumnName("ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.CATEGORIE)
            .HasColumnName("CATEGORIE")
            .IsRequired(true);

        builder.Property(x => x.CODE_CONSTANTE)
            .HasColumnName("CODE_CONSTANTE")
            .IsRequired(true);

        builder.Property(x => x.INTITULE)
            .HasColumnName("INTITULE")
            .IsRequired(false);
    }
}
