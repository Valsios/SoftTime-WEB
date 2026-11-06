using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_CODEABSENCEConfiguration : IEntityTypeConfiguration<T_CODEABSENCE>
{
    public void Configure(EntityTypeBuilder<T_CODEABSENCE> builder)
    {
        builder.ToTable("T_CODEABSENCE");
        builder.HasKey(x => x.ID_ABSENCE);

        builder.Property(x => x.ID_ABSENCE)
            .HasColumnName("ID_ABSENCE")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.INTITULE_ABSENCE)
            .HasColumnName("INTITULE_ABSENCE")
            .IsRequired(false);

        builder.Property(x => x.NOT_PAY)
            .HasColumnName("NOT_PAY")
            .IsRequired(false);
    }
}
