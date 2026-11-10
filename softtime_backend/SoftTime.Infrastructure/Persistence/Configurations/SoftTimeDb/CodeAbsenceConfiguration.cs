using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Persistence.Configurations.SoftTimeDb;

public sealed class CodeAbsenceConfiguration : IEntityTypeConfiguration<CodeAbsence>
{
    public void Configure(EntityTypeBuilder<CodeAbsence> builder)
    {
        builder.ToTable("T_CODEABSENCE");
        builder.HasKey(x => x.IdAbsence);
        ConfigureTextColumns(builder);
        ConfigureValueColumns(builder);
    }

    private static void ConfigureTextColumns(EntityTypeBuilder<CodeAbsence> builder)
    {
        builder.Property(x => x.IntituleAbsence).HasColumnName("INTITULE_ABSENCE").IsRequired(false);
    }

    private static void ConfigureValueColumns(EntityTypeBuilder<CodeAbsence> builder)
    {
        builder.Property(x => x.IdAbsence).HasColumnName("ID_ABSENCE").ValueGeneratedOnAdd();
        builder.Property(x => x.NotPay).HasColumnName("NOT_PAY").IsRequired(false);
    }
}
