using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class T_SemaineConfiguration : IEntityTypeConfiguration<T_Semaine>
{
    public void Configure(EntityTypeBuilder<T_Semaine> builder)
    {
        builder.ToTable("T_Semaine");
        builder.HasNoKey();

        builder.Property(x => x.CodeSemain)
            .HasColumnName("CodeSemain")
            .IsRequired(false);

        builder.Property(x => x.DateDeb)
            .HasColumnName("DateDeb")
            .IsRequired(false);

        builder.Property(x => x.DateFin)
            .HasColumnName("DateFin")
            .IsRequired(false);
    }
}
